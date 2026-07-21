using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.FeatureManagement.Mvc;
using Philips.IBE.Service.WebAgent.Server.DBUtilities;
using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Utilities;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Philips.IBE.Service.WebAgent.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [FeatureGate("DatabaseEnabled")]
    [Authorize]
    public class ErrorQueueController : ControllerBase
    {
        private readonly IDBUtils _dBUtils;
        private readonly ILogger<ErrorQueueController> _logger;

        public ErrorQueueController(IDBUtils dBUtils, ILogger<ErrorQueueController> logger)
        {
            _dBUtils = dBUtils;
            _logger = logger;
        }

        // GET: api/<ErrorQueueController>
        [HttpGet]
        public ActionResult<ErrorQueue> Get()
        {
            _logger.LogInformation("Fetching error queue.");
            var errorQueue = _dBUtils.FetchErrorQueue();
            
            if (errorQueue == null)
            {
                _logger.LogError("Failed to fetch error queue - query returned null.");
                return StatusCode(500, "Failed to fetch error queue.");
            }
            
            if (!errorQueue.Any())
            {
                _logger.LogInformation("Error queue is empty.");
                return Ok(new { message = "Error queue is empty.", data = errorQueue });
            }
            
            _logger.LogInformation("Error queue fetched successfully with {Count} items.", errorQueue.Count);
            return Ok(errorQueue);
        }

        // PUT api/<ErrorQueueController>/5
        [HttpPut("{id}")]
        public ActionResult UpdateStatus(int id)
        {
            _logger.LogInformation("Updating status for ID: {Id}", id);

            if (id <= 0)
            {
                _logger.LogWarning("Invalid ID: {Id}", id);
                return BadRequest("Invalid ID.");
            }

            var result = _dBUtils.UpdateStatus("PROCESS_REQUESTED", id.ToString());
            if (result)
            {
                _logger.LogInformation("Status updated successfully for ID: {Id}", id);
                return Ok("Status updated successfully.");
            }

            _logger.LogError("Failed to update status for ID: {Id}", id);
            return StatusCode(500, "Failed to update status.");
        }
    }
}
