using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Services;

namespace Philips.IBE.Service.WebAgent.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ServiceNodeController : ControllerBase
    {
        private readonly INodeService _serviceNodeService;
        private readonly ILogger<ServiceNodeController> _logger;

        public ServiceNodeController(INodeService serviceNodeService, ILogger<ServiceNodeController> logger)
        {
            _serviceNodeService = serviceNodeService;
            _logger = logger;
        }

        [HttpGet]
        [Authorize]
        public ActionResult<ServiceNode> GetAllServiceNodes()
        {
            _logger.LogInformation("Fetching all service nodes.");
            try
            {
                var data = _serviceNodeService.GetServiceNode();
                _logger.LogInformation("Successfully fetched all service nodes.");
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching all service nodes.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error.");
            }
        }

        [HttpPost("http")]
        public ActionResult<bool> UpdateHTTPServiceNode([FromBody] ServiceNodeConfiguration serviceNodeConfiguration)
        {
            _logger.LogInformation("Updating HTTP service node.");
            try
            {
                var result = _serviceNodeService.UpdateHTTPServiceNode(serviceNodeConfiguration);
                if (result)
                {
                    _logger.LogInformation("Successfully updated HTTP service node.");
                    return Ok(true);
                }
                _logger.LogWarning("Failed to update HTTP service node.");
                return BadRequest(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating HTTP service node.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error.");
            }
        }

        [HttpPost("tcp")]
        public ActionResult<bool> UpdateTCPServiceNode([FromBody] ServiceNodeConfiguration serviceNodeConfiguration)
        {
            _logger.LogInformation("Updating TCP service node.");
            try
            {
                var result = _serviceNodeService.UpdateTCPServiceNode(serviceNodeConfiguration);
                if (result)
                {
                    _logger.LogInformation("Successfully updated TCP service node.");
                    return Ok(true);
                }
                _logger.LogWarning("Failed to update TCP service node.");
                return BadRequest(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating TCP service node.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error.");
            }
        }

        [HttpPost("websocket")]
        public ActionResult<bool> UpdateWebSocketServiceNode([FromBody] ServiceNodeConfiguration serviceNodeConfiguration)
        {
            _logger.LogInformation("Updating WebSocket service node.");
            try
            {
                var result = _serviceNodeService.UpdateWebSocketClientServiceNode(serviceNodeConfiguration);
                if (result)
                {
                    _logger.LogInformation("Successfully updated WebSocket service node.");
                    return Ok(true);
                }
                _logger.LogWarning("Failed to update WebSocket service node.");
                return BadRequest(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating WebSocket service node.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error.");
            }
        }

        [HttpPost("adt")]
        public ActionResult<bool> UpdateADTServiceNode([FromBody] ServiceNodeConfiguration serviceNodeConfiguration)
        {
            _logger.LogInformation("Updating ADT service node.");
            try
            {
                var result = _serviceNodeService.UpdateADTServiceNode(serviceNodeConfiguration);
                if (result)
                {
                    _logger.LogInformation("Successfully updated ADT service node.");
                    return Ok(true);
                }
                _logger.LogWarning("Failed to update ADT service node.");
                return BadRequest(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating ADT service node.");
                return StatusCode(StatusCodes.Status500InternalServerError, "Internal server error.");
            }
        }
    }
}
