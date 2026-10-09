using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Services.Players;

// Interleave players and share a set ID to catch truncation, cross-player mixing,
// and accidental global deduplication of matchups.
FlatBracketPathEntrantCards[] rows = [Row(10, 101), Row(20, 101), Row(10, 102), Row(30, 301), Row(20, 202)];
var groups = PlayerQueryService.MapBracketPaths(rows);
Check(groups.Count == 3, "one group for each player");
foreach (var group in groups)
{
    var id = group.PlayerTournamentCard.PlayerID;
    var expected = rows.Where(r => r.PlayerId == id).ToArray();
    Check(group.EntrantSetCards.Select(c => c.SetID).SequenceEqual(expected.Select(r => r.SetId.ToString())), "all sets retained for their player");
    Check(group.EntrantSetCards.All(c => c.PlayerOneID == id && c.PlayerTwoID == 99), "player identities retained");
    Check(group.PlayerTournamentCard.PlayerResults.Count == 1, "actual standing returned once per path");
    Check(group.TournamentLinkID == id * 100 && group.TournamentName == $"Tournament {id}", "group metadata retained");
}
Check(groups.Sum(g => g.EntrantSetCards.Count) == rows.Length, "no rows lost or duplicated");
Check(PlayerQueryService.MapBracketPaths(rows.Where(r => r.PlayerId == 10)).Count == 1, "one player returns one group");
Check(PlayerQueryService.MapBracketPaths([]).Count == 0, "no matches returns empty list");
// Empty input must return before trying to connect.
var service = (PlayerQueryService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PlayerQueryService));
Check((await service.GetBracketPathByPlayerIds([])).Count == 0, "empty input returns empty list");
var secondPath = Row(10, 103);
secondPath.BracketPathId = 999;
secondPath.TournamentLink = 555;
var separated = PlayerQueryService.MapBracketPaths(rows.Append(secondPath));
Check(separated.Count == 4 && separated.Single(x => x.BracketPathId == 999).TournamentLinkID == 555, "same player multiple paths never merge metadata");
var emptyPath = Row(10, 0); emptyPath.BracketPathId = 888; emptyPath.Placement = null;
var empty = PlayerQueryService.MapBracketPaths([emptyPath]).Single();
Check(empty.EntrantSetCards.Count == 0 && empty.PlayerTournamentCard.PlayerResults.Count == 0, "empty path survives without fabricated standing");
Check(StartggNormalization.Normalize("https://start.gg/tournament/evo-france-2026/event/street-fighter-6-ps5") == "tournament/evo-france-2026/event/street-fighter-6-ps5", "full event URL");
Check(StartggNormalization.Normalize("street-fighter-6-ps5") == "street-fighter-6-ps5", "event suffix");
try { StartggNormalization.Normalize("https://start.gg/tournament/evo-france-2026"); throw new Exception("parent accepted"); } catch (ArgumentException) { }
Console.WriteLine("PASS multi-player grouping, complete row retention, single-player grouping, and empty results/input.");

static FlatBracketPathEntrantCards Row(int player, int set) => new()
{
    BracketPathId = player, PlayerName = $"Player {player}", Placement = 2, PlayerId = player, SetId = set, PlayerOneId = player, PlayerTwoId = 99,
    PlayerOneName = $"Player {player}", PlayerTwoName = "Opponent",
    TournamentLink = player * 100, TournamentName = $"Tournament {player}",
    EventLink = player * 1000, RoundNum = "Finals", EntrantOneId = player + 1, EntrantTwoId = 100
};
static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}


