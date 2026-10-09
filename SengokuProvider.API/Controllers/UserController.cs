using Microsoft.AspNetCore.RateLimiting;
using SengokuProvider.Library.Workflows.Users;
using Microsoft.AspNetCore.Mvc;
using SengokuProvider.Library.Models.User;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Users;

namespace SengokuProvider.API.Controllers
{
    [ApiController]
    [Route("api/user/")]
    public class UserController : Controller
    {
        private readonly ILogger<UserController> _log;
        private readonly IUserService _userService;
        private readonly IUserOperations _userOperations;
        private readonly CommandProcessor _commandProcessor;
        public UserController(ILogger<UserController> logger, IUserService userService, IUserOperations userOperations, CommandProcessor commandProcessor)
        {
            _log = logger;
            _userService = userService;
            _userOperations = userOperations;
            _commandProcessor = commandProcessor;
        }

        [HttpPost("GetSearchedUsers")]
        public async Task<IActionResult> GetSearchedUsers()
        {
            return new ObjectResult("Request was not valid");
        }
        [HttpPost("CreateUser")]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("account-entry")]
        [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        public async Task<IActionResult> CreateNewUser([FromBody] CreateUserCommand command)
        {
            if (command == null)
            {
                _log.LogError("CreateTable command is null.");
                return new BadRequestObjectResult("Command cannot be null.") { StatusCode = StatusCodes.Status400BadRequest };
            }

            var parsedRequest = await _commandProcessor.ParseRequest(command);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.StartsWith("BadRequest", StringComparison.Ordinal))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }

            try
            {
                var result = await _userService.CreateUser(parsedRequest.UserName, parsedRequest.Email, parsedRequest.Password);
                if (result > 0)
                {
                    var user = await _userService.GetUserById(result);
                    if (user?.PlayerId is not > 0) throw new InvalidOperationException("Registration has no local player.");
                    return Ok(new { userId = result, playerId = user.PlayerId.Value,
                        response = $"User {parsedRequest.UserName} created successfully." });
                }
                return Conflict(new { response = "Unable to create an account with those details." });


            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { response = ex.Message });
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error creating user.");
                return new ObjectResult("Error message") { StatusCode = StatusCodes.Status500InternalServerError };

            }
        }
        [HttpPost("SyncStartggDataToPlayer")]
        public async Task<IActionResult> SyncStartggDataToUserData([FromBody] SyncStartggToUserCommand cmd)
        {
            if (cmd == null)
            {
                _log.LogError("Command is null");
                return new BadRequestObjectResult("Command cannot be null.") { StatusCode = StatusCodes.Status400BadRequest };
            }
            var parsedRequest = await _commandProcessor.ParseRequest(cmd);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.StartsWith("BadRequest", StringComparison.Ordinal))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }

            UserPlayerDataResponse result = await _userOperations.SyncStartggDataToUserData(cmd.PlayerName, cmd.UserSlug);
            return Ok(result);
        }
    }
}
