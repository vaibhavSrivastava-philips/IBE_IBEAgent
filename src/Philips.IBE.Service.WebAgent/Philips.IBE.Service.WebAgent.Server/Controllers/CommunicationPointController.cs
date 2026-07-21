using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Philips.IBE.Service.WebAgent.Server.Exceptions;
using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Services;

namespace Philips.IBE.Service.WebAgent.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CommunicationPointController : ControllerBase
    {
        private readonly ICommunicationDataService _communicationDataService;
        private readonly ILogger<CommunicationPointController> _logger;

        public CommunicationPointController(ICommunicationDataService communicationDataService, ILogger<CommunicationPointController> logger)
        {
            _communicationDataService = communicationDataService;
            _logger = logger;
        }

        [HttpGet]
        [Authorize]
        public ActionResult<List<CommunicationPoint>> GetAllCommunicationPoints()
        {
            _logger.LogInformation("Fetching all communication points.");
            try
            {
                var data = _communicationDataService.GetAllCommunicationData();
                _logger.LogInformation("Successfully fetched all communication points.");
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching all communication points.");
                return StatusCode(500, "Internal server error.");
            }
        }

        [HttpGet("{id}")]
        public ActionResult<CommunicationPoint> GetCommunicationPointById(int id)
        {
            _logger.LogInformation("Fetching communication point with ID: {Id}", id);
            try
            {
                var data = _communicationDataService.GetCommunicationDataById(id);
                if (data == null)
                {
                    _logger.LogWarning("Communication point with ID {Id} not found.", id);
                    return NotFound($"Communication point with ID {id} not found.");
                }
                _logger.LogInformation("Successfully fetched communication point with ID: {Id}", id);
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching communication point with ID: {Id}", id);
                return StatusCode(500, "Internal server error.");
            }
        }

        [Authorize(Roles = "admin")]
        [HttpPost]
        public IActionResult AddCommunicationPoint([FromBody] CommunicationPoint data)
        {
            if (data == null)
            {
                _logger.LogWarning("Invalid communication point data.");
                return BadRequest("Invalid communication point data.");
            }

            _logger.LogInformation("Adding new communication point.");
            try
            {
                _communicationDataService.AddCommunicationData(data);
                _logger.LogInformation("Successfully added new communication point.");
                return Ok("Communication point has been added successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while adding a new communication point.");
                return StatusCode(500, "Internal server error.");
            }
        }

        [Authorize(Roles = "admin")]
        [HttpPut("{id}")]
        public IActionResult UpdateCommunicationPoint(int id, [FromBody] CommunicationPoint updatedData)
        {
            if (updatedData == null)
            {
                _logger.LogWarning("Invalid communication point data.");
                return BadRequest("Invalid communication point data.");
            }

            _logger.LogInformation("Updating communication point with ID: {Id}", id);
            try
            {
                _communicationDataService.UpdateCommunicationData(id, updatedData);
                _logger.LogInformation("Successfully updated communication point with ID: {Id}", id);
                return Ok("Communication point has been updated successfully.");
            }
            catch (DataNotFoundException ex)
            {
                _logger.LogWarning(ex, "Communication point with ID {Id} not found.", id);
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating communication point with ID: {Id}", id);
                return StatusCode(500, "Internal server error.");
            }
        }

        [Authorize(Roles = "admin")]
        [HttpDelete("{id}")]
        public IActionResult DeleteCommunicationPoint(int id)
        {
            _logger.LogInformation("Deleting communication point with ID: {Id}", id);
            try
            {
                _communicationDataService.DeleteCommunicationData(id);
                _logger.LogInformation("Successfully deleted communication point with ID: {Id}", id);
                return Ok("Communication point has been deleted successfully.");
            }
            catch (DataNotFoundException ex)
            {
                _logger.LogWarning(ex, "Communication point with ID {Id} not found.", id);
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while deleting communication point with ID: {Id}", id);
                return StatusCode(500, "Internal server error.");
            }
        }
    }
}