using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Philips.IBE.Service.WebAgent.Server.Services;

namespace Philips.IBE.Service.WebAgent.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class HeartBeatController : ControllerBase
    {
        private readonly IHeartBeatService _heartBeatService;
        private readonly ILogger<HeartBeatController> _logger;

        public static string SanitizeLogUserInput(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // Remove control characters
            var sanitized = new string(input.Where(c => !char.IsControl(c)).ToArray());

            // Replace dangerous characters
            sanitized = sanitized.Replace("[", "")
                                 .Replace("]", "")
                                 .Replace("\"", "")
                                 .Replace("'", "")
                                 .Replace("{", "")
                                 .Replace("}", "")
                                 .Replace(";", "")
                                 .Replace("|", "");

            // Optionally, limit length
            if (sanitized.Length > 100)
                sanitized = sanitized.Substring(0, 100);

            return sanitized;
        }

        public HeartBeatController(IHeartBeatService heartBeatService, ILogger<HeartBeatController> logger)
        {
            _heartBeatService = heartBeatService;
            _logger = logger;
        }

        [HttpGet("server")]
        public async Task<IActionResult> GetServerStatus([FromQuery] string host, [FromQuery] int port)
        {
            var sanitizedHost = SanitizeLogUserInput(host);
            _logger.LogInformation("GetServerStatus request initiated with host: {Host}, port: {Port}", sanitizedHost, port);

            if (string.IsNullOrEmpty(host) || port <= 0 || port > 65535)
            {
                _logger.LogWarning("Invalid host or port: {Host}, {Port}", sanitizedHost, port);
                return BadRequest("Invalid host or port.");
            }

            try
            {
                bool isOpen = await _heartBeatService.IsPortOpenAsync(host, port);
                var result = new
                {
                    Host = sanitizedHost,
                    Port = port,
                    IsOpen = isOpen,
                    Status = isOpen ? "open" : "closed"
                };

                _logger.LogInformation("GetServerStatus request completed for host: {Host}, port: {Port}, status: {Status}", sanitizedHost, port, result.Status);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while checking server status for host: {Host}, port: {Port}", sanitizedHost, port);
                return StatusCode(500, "Internal server error.");
            }
        }

        [HttpGet("client")]
        public IActionResult GetClientTcpPorts()
        {
            _logger.LogInformation("GetClientTcpPorts request initiated");

            try
            {
                var tcpLines = _heartBeatService.GetTcpPorts();
                _logger.LogInformation("GetClientTcpPorts request completed successfully");
                return Ok(new { TcpPorts = tcpLines });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving TCP ports");
                return StatusCode(500, new { message = "An error occurred while running netstat", error = ex.Message });
            }
        }
    }
}
