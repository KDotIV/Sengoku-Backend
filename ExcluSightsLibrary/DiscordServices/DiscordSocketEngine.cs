using Discord;
using Discord.WebSocket;
using ExcluSightsLibrary.DiscordModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExcluSightsLibrary.DiscordServices
{
    public class DiscordSocketEngine : ISocketEngine
    {
        private readonly DiscordSocketClient _client;
        private readonly string _token;
        private readonly ILogger<DiscordSocketEngine> _log;
        private readonly IServiceScopeFactory _scopeFactory;
        private ICustomerIntakeService _customerIntake => _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ICustomerIntakeService>();
        private ICustomerQueryService _customerQuery => _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<ICustomerQueryService>();


        private int _started;
        private readonly SemaphoreSlim _gate = new(1, 1);

        // barrier for the first complete backfill
        private TaskCompletionSource<bool> _initialBackfillTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _backfillStarted;   // avoid re-running on reconnect
        private volatile bool _backfillCompleted; // keeps initial guild state

        public DiscordSocketEngine(string botToken, string connStr, ILogger<DiscordSocketEngine> log, IServiceScopeFactory scopeFactory)
        {
            _token = botToken!;
            _log = log;
            _client = CreateClient();
            _scopeFactory = scopeFactory;
            WireEventHandlers();
        }

        private void WireEventHandlers()
        {
            _client.Log += OnLogAsync;
            _client.Ready += OnReadyAsync;
            _client.GuildMemberUpdated += OnGuildMemberUpdateAsync;
            _client.UserJoined += OnUserJoinedAsync;
        }

        private DiscordSocketClient CreateClient()
        {
            return new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers,
                AlwaysDownloadUsers = false,
                LogLevel = LogSeverity.Info
            });
        }
        #region Public API
        public async Task EnsureStartedAsync()
        {
            if (Interlocked.CompareExchange(ref _started, 1, 0) == 1)
                return;

            await _gate.WaitAsync();
            try
            {
                if (_client.LoginState != LoginState.LoggedIn)
                {
                    await _client.LoginAsync(TokenType.Bot, _token);
                    await _client.StartAsync();
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        public async Task<bool> WaitForInitialBackfillAsync(TimeSpan timeout)
        {
            // return if already filled
            if (_backfillCompleted) return true;

            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await _initialBackfillTcs.Task.WaitAsync(cts.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                _log.LogWarning("InitialBackfill timed out after {Timeout}. Proceeding without full backfill.", timeout);
                return false;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "WaitForInitialBackfillAsync failed.");
                return false;
            }
        }
        public IReadOnlyList<(ulong GuildId, string GuildName)> GetConnectedGuilds() => _client.Guilds.Select(g => (g.Id, g.Name)).ToList();
        public IReadOnlyList<DiscordRoleData> GetRolesForConnectedGuild(ulong guildId)
        {
            var guild = _client.GetGuild(guildId);
            if (guild is null) return Array.Empty<DiscordRoleData>();
            return guild.Roles.Select(r => new DiscordRoleData
            {
                RoleId = r.Id,
                RoleName = r.Name,
                IsManaged = r.IsManaged,
            }).ToList();
        }
        public async Task<bool> DownloadGuildMembersAsync(ulong guildId)
        {
            var guild = _client.GetGuild(guildId);
            if (guild is null) return false;

            await guild.DownloadUsersAsync();
            return true;
        }
        #endregion
        #region Event Handlers
        private Task OnLogAsync(LogMessage msg)
        {
            var severity = msg.Severity switch
            {
                LogSeverity.Critical => LogLevel.Critical,
                LogSeverity.Error => LogLevel.Error,
                LogSeverity.Warning => LogLevel.Warning,
                LogSeverity.Info => LogLevel.Information,
                LogSeverity.Verbose => LogLevel.Debug,
                LogSeverity.Debug => LogLevel.Trace,
                _ => LogLevel.None
            };
            _log.Log(severity, "[Discord] {Msg}", msg.ToString());
            return Task.CompletedTask;
        }
        private async Task OnReadyAsync()
        {
            _log.LogInformation("Discord Ready as {User}", _client.CurrentUser);

            if (_backfillCompleted || _backfillStarted)
            {
                _log.LogInformation("Initial backfill already handled (started={Started}, completed={Completed}).",
                    _backfillStarted, _backfillCompleted);
                return;
            }

            _backfillStarted = true;
            await InitializeBackfillAsync();
        }
        private async Task InitializeBackfillAsync()
        {
            try
            {
                // kick off downloads for all guilds
                var downloads = _client.Guilds.Select(g =>
                {
                    _log.LogInformation("Starting member download for Guild: {Name} ({Id})", g.Name, g.Id);
                    return g.DownloadUsersAsync();
                }).ToArray();

                // wait for them to finish
                await Task.WhenAll(downloads);

                // mark complete
                _backfillCompleted = true;
                _initialBackfillTcs.TrySetResult(true);
                _log.LogInformation("Initial member backfill completed for {Count} guild(s).", _client.Guilds.Count);

                // process all role changes
                // NOTE: this is a potentially large operation, consider streaming via Channels to a background writer
                Task[] roleChanges = _client.Guilds.Select(g =>
                {
                    _log.LogInformation("Processing role changes for Guild: {Name} ({Id})", g.Name, g.Id);
                    return _customerIntake.UpsertGuildRoleChangesAsync(g.Id, g.Roles);
                }).ToArray();

                await Task.WhenAll(roleChanges);

                // process all members in all guilds
                var whiteList = await _customerQuery.GetServerRoleWhiteList(_client.Guilds.First().Id);
                List<SolePlayDTO> reducedMembers = await ReduceMembersAsync(guildId: null, whiteList,
                    customerIdFactory: () => _customerIntake.GenerateNewCustomerID(), ct: CancellationToken.None);

                int results = await _customerIntake.SaveGuildMemberChangesAsync(reducedMembers);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Initial member backfill failed.");
                _initialBackfillTcs.TrySetException(ex);
            }
        }

        private async Task<List<SolePlayDTO>> ReduceMembersAsync(
            ulong? guildId,
            IReadOnlyCollection<RoleMapping> whitelist,
            Func<Task<string>> customerIdFactory,
            CancellationToken ct = default)
        {
            // Select the guild set
            IEnumerable<SocketGuild> guilds = guildId.HasValue
                ? new[] { _client.GetGuild(guildId.Value) }.Where(g => g != null)!
                : _client.Guilds;

            var roleDict = whitelist.ToDictionary(r => r.RoleId, r => r);

            var result = new List<SolePlayDTO>(capacity: 1024);

            foreach (var g in guilds)
            {
                if (g is null) continue;

                foreach (var user in g.Users)
                {
                    if (ct.IsCancellationRequested) break;
                    if (user.IsBot) continue;

                    int? gender = null;
                    double? shoe = null;
                    var interests = Interests.None;

                    // Merge attributes from all matched roles
                    foreach (var role in user.Roles)
                    {
                        if (!roleDict.TryGetValue(role.Id, out var found))
                            continue;

                        gender = found.RoleName.Equals("Men", StringComparison.OrdinalIgnoreCase) ? 1 : gender;
                        gender = found.RoleName.Equals("Ladies", StringComparison.OrdinalIgnoreCase) ? 2 : gender;

                        if (double.TryParse(found.RoleName, out var shoeSize))
                            shoe = shoe.HasValue ? Math.Max(shoe.Value, shoeSize) : shoeSize;
                    }

                    // Skip users with no relevant roles
                    if (!gender.HasValue && !shoe.HasValue && interests == Interests.None)
                        continue;

                    // Use factory delegate to get/generate customer ID
                    var customerId = await customerIdFactory().ConfigureAwait(false);

                    result.Add(new SolePlayDTO
                    {
                        CustomerId = customerId,
                        DiscordId = user.Id,
                        DiscordTag = $"{user.Username}#{user.Discriminator}",
                        CustomerFirstName = user.DisplayName,
                        CustomerLastName = null,
                        ShoeSize = shoe ?? 0,
                        Interests = interests,
                        Gender = gender ?? 0,
                        LastUpdated = DateTime.UtcNow
                    });
                }
            }

            return result;
        }
        private async Task OnGuildMemberUpdateAsync(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
        {
            try
            {
                var oldRoles = (await before.GetOrDownloadAsync())?.Roles?.Select(r => (long)r.Id).ToHashSet()
                    ?? new HashSet<long>();
                var newRoles = after.Roles?.Select(r => (long)r.Id).ToHashSet();

                if (newRoles == null)
                {
                    _log.LogWarning("OnGuildMemberUpdateAsync: newRoles is null for User: {User} ({Id})", after.Username, after.Id);
                    return;
                }
                if (oldRoles.SetEquals(newRoles))
                {
                    // no change in roles
                    return;
                }
                var addedRoles = newRoles.Except(oldRoles).ToList();
                var removedRoles = oldRoles.Except(newRoles).ToList();

                _log.LogInformation("User: {User} ({Id}) Roles changed. Added: [{Added}], Removed: [{Removed}]",
                    after.Username, after.Id,
                    string.Join(", ", addedRoles),
                    string.Join(", ", removedRoles));

                _log.LogInformation("Saving role changes to DB for User: {User} ({Id})", after.Username, after.Id);

                await SaveRoleChangesAsync(after, addedRoles, removedRoles);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "OnGuildMemberUpdateAsync failed for User: {User} ({Id})", after.Username, after.Id);
            }
            await Task.CompletedTask;
        }
        private async Task OnUserJoinedAsync(SocketGuildUser user)
        {
            try
            {
                string newCustId = "CUST_678";
                if (!await _customerIntake.UpsertDiscordAccountAsync(user.Id, user.Username, user.Discriminator, newCustId))
                {
                    _log.LogWarning("OnUserJoinedAsync: Failed to upsert Discord account for User: {User} ({Id})", user.Username, user.Id);
                    return;
                }
                _log.LogInformation("OnUserJoinedAsync: Processed new user {User} ({Id})", user.Username, user.Id);
                await _customerIntake.UpsertMembershipAsync(user.Guild.Id, user.Id, DateTime.UtcNow);
                await _customerIntake.UpsertSolePlayFromRolesAsync(user.Guild.Id, user.Id);
                await _customerIntake.SyncCustomerInterestsFromRolesAsync(user.Guild.Id, user.Id);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "OnUserJoinedAsync failed for User: {User} ({Id})", user.Username, user.Id);
            }
            await Task.CompletedTask;
        }
        #endregion
        #region WriteToDB
        private async Task SaveRoleChangesAsync(SocketGuildUser after, List<long> addedRoles, List<long> removedRoles)
        {
            try
            {
                string newCustId = await _customerIntake.GenerateNewCustomerID();
                if (!await _customerIntake.UpsertDiscordAccountAsync(after.Id, after.Username, after.Discriminator, newCustId))
                {
                    _log.LogWarning("OnUserJoinedAsync: Failed to upsert Discord account for User: {User} ({Id})", after.Username, after.Id);
                    return;
                }
                _log.LogInformation("OnUserJoinedAsync: Processed new after {User} ({Id})", after.Username, after.Id);
                await _customerIntake.UpsertMembershipAsync(after.Guild.Id, after.Id, DateTime.UtcNow);
                await _customerIntake.UpsertSolePlayFromRolesAsync(after.Guild.Id, after.Id);
                await _customerIntake.SyncCustomerInterestsFromRolesAsync(after.Guild.Id, after.Id);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "OnUserJoinedAsync failed for User: {User} ({Id})", after.Username, after.Id);
            }
        }

        public Task<IReadOnlyList<CustomerProfileData>> GetCustomersDataByGuildIdAsync(ulong guildId, string? email)
        {
            return _customerQuery.GetCustomersDataByGuildId(guildId, email);
        }
        #endregion
        #region Helpers
        #endregion
    }
}
