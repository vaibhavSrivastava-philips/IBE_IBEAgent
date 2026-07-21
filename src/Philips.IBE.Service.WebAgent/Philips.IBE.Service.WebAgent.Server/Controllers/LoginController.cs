using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Philips.IBE.Service.WebAgent.Server.Constants;
using System.Net;
using System.IdentityModel.Tokens.Jwt;
using Philips.IBE.Service.WebAgent.Server.Authentication;

namespace Philips.IBE.Service.WebAgent.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LoginController : ControllerBase
    {
        private readonly IAuthenticationService _loginService;
        private readonly JWTInvalidator _jwtInvalidator;
        private readonly ILogger<LoginController> _logger;

        public LoginController(IAuthenticationService loginService, JWTInvalidator jwtInvalidator, ILogger<LoginController> logger)
        {
            _loginService = loginService;
            _jwtInvalidator = jwtInvalidator;
            _logger = logger;
        }

        [HttpPost]
        public ActionResult<ResponseModel> Login()
        {
            _logger.LogInformation("Login request initiated");
            string[] credentials = GetDecodedBasicAuthHeader();
            if (credentials.Length != 2)
            {
                _logger.LogWarning("Invalid credentials format");
                return BadRequest(new ResponseModel { Status = Status.Failure, DisplayMessage = "Invalid credentials" });
            }

            var response = _loginService.LoginUser(credentials[0], credentials[1]);
            if (response.Status != Status.Successful)
            {
                _logger.LogWarning("Unauthorized login attempt");
                return Unauthorized(response);
            }

            _logger.LogInformation("Login successful");
            return Ok(response);
        }

        [HttpPost("logout")]
        public IActionResult Logout()
        {
            _logger.LogInformation("Logout request initiated");
            var token = Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();

            if (token != null)
            {
                var expiry = GetTokenExpiry(token);
                _jwtInvalidator.AddToken(token, expiry);
                _logger.LogInformation("Token invalidated successfully");
            }

            return Ok(new { message = "Logged out successfully" });
        }

        private DateTime GetTokenExpiry(string token)
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);
            return jwtToken.ValidTo;
        }

        private string[] GetDecodedBasicAuthHeader()
        {
            try
            {
                string encodedString = GetBasicAuthHeader();
                byte[] data = Convert.FromBase64String(encodedString);
                return System.Text.Encoding.UTF8.GetString(data).Split(":");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error decoding Basic Auth header : {ExceptionMessage}", ex);
                return Array.Empty<string>();
            }
        }

        private string GetBasicAuthHeader()
        {
            try
            {
                var authHeader = Request.Headers["Authorization"];
                return authHeader.ToString().Substring("Basic ".Length).Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrieving Basic Auth header : {ExceptionMessage}", ex);
                throw;
            }
        }
    }
}
