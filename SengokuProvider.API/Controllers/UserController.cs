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
        private readonly CommandProcessor _commandProcessor;
        public UserController(ILogger<UserController> logger, IUserService userService, CommandProcessor commandProcessor)
        {
            _log = logger;
            _userService = userService;
            _commandProcessor = commandProcessor;
        }

        [HttpPost("GetSearchedUsers")]
        public async Task<IActionResult> GetSearchedUsers()
        {
            return new ObjectResult("Request was not valid");
        }
        [HttpPost("CreateUser")]
        public async Task<IActionResult> CreateNewUser([FromBody] CreateUserCommand command)
        {
            if (command == null)
            {
                _log.LogError("CreateTable command is null.");
                return new BadRequestObjectResult("Command cannot be null.") { StatusCode = StatusCodes.Status400BadRequest };
            }

            var parsedRequest = await _commandProcessor.ParseRequest(command);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }

            try
            {
                var result = await _userService.CreateUser(parsedRequest.UserName, parsedRequest.Email, parsedRequest.Password);
                if (result > 0) return new OkObjectResult($"User {parsedRequest.UserName} created successfully.");

                _log.LogError("Create User execution failed.");
                return new ObjectResult("Error message") { StatusCode = StatusCodes.Status500InternalServerError };
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
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _log.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }

            UserPlayerDataResponse result = await _userService.SyncStartggDataToUserData(cmd.PlayerName, cmd.UserSlug);
            return Ok(result);
        }
    }
}
