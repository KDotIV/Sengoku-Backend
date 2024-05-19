using Microsoft.AspNetCore.Mvc;
using SengokuProvider.API.Models.User;
using SengokuProvider.API.Services.Common;
using SengokuProvider.API.Services.Users;

namespace SengokuProvider.API.Controllers
{
    [ApiController]
    [Route("api/user/")]
    public class UserController : Controller
    {
        private readonly ILogger<UserController> _logger;
        private readonly IUserService _userService;
        private readonly CommandProcessor _commandProcessor;
        public UserController(ILogger<UserController> logger, IUserService userService, CommandProcessor commandProcessor)
        {
            _logger = logger;
            _userService = userService;
            _commandProcessor = commandProcessor;
        }

        [HttpGet("GetSearchedUsers")]
        public async Task<IActionResult> GetSearchedUsers()
        {
            return new ObjectResult("Request was not valid");
        }
        [HttpPost("CreateUser")]
        public async Task<IActionResult> CreateNewUser([FromBody] CreateUserCommand command)
        {
            if (command == null)
            {
                _logger.LogError("CreateTable command is null.");
                return new BadRequestObjectResult("Command cannot be null.") { StatusCode = StatusCodes.Status400BadRequest };
            }

            var parsedRequest = await _commandProcessor.ParseRequest(command);
            if (!string.IsNullOrEmpty(parsedRequest.Response) && parsedRequest.Response.Equals("BadRequest"))
            {
                _logger.LogError($"Request parsing failed: {parsedRequest.Response}");
                return new BadRequestObjectResult(parsedRequest.Response);
            }

            try
            {
                var result = await _userService.CreateUser(parsedRequest.UserName, parsedRequest.Email, parsedRequest.Password);
                if (result > 0) return new OkObjectResult($"User {parsedRequest.UserName} created successfully.");

                _logger.LogError("Create User execution failed.");
                return new ObjectResult("Error message") { StatusCode = StatusCodes.Status500InternalServerError };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user.");
                return new ObjectResult("Error message") { StatusCode = StatusCodes.Status500InternalServerError };

            }
        }
    }
}
