using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Services;
using Philips.IBE.Service.WebAgent.Server.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace Philips.IBE.Service.WebAgent.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "admin")]
    public class ContractController : ControllerBase
    {
        private readonly IContractService _contractService;
        private readonly ILogger<ContractController> _logger;

        private string SanitizeForLog(string input)
        {
            if (input == null) return string.Empty;
            return input.Replace("\r", "").Replace("\n", "");
        }

        public ContractController(IContractService contractService, ILogger<ContractController> logger)
        {
            _contractService = contractService;
            _logger = logger;
        }

        [HttpGet]
        public ActionResult<List<Contract>> GetAllContracts()
        {
            _logger.LogInformation("Get All Contracts request initiated");
            try
            {
                var contracts = _contractService.GetAllContracts();
                return Ok(contracts);
            }
            catch (Exception ex)
            {
                _logger.LogError("An error occurred while fetching contracts : {ExceptionMessage}", ex);
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        //[HttpGet("{id}")]
        //public ActionResult<Contract> GetContractById(int id)
        //{
        //    _logger.LogInformation($"Get Contract by ID request initiated for ID: {id}");
        //    try
        //    {
        //        var contract = _contractService.GetContractById(id);
        //        if (contract == null)
        //        {
        //            return NotFound($"Contract with ID {id} not found.");
        //        }
        //        return Ok(contract);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError("An error occurred while fetching the contract", ex);
        //        return StatusCode(500, $"Internal server error: {ex.Message}");
        //    }
        //}

        [HttpPost]
        public IActionResult AddContract([FromBody] Contract contract)
        {
            _logger.LogInformation("Add Contract request initiated");
            if (contract == null)
            {
                _logger.LogWarning("Invalid contract data");
                return BadRequest("Invalid contract data.");
            }

            try
            {
                _contractService.AddContract(contract);
                return Ok("Contract has been added successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError("An error occurred while adding the contract : {ExceptionMessage}", ex);
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpPut("{name}")]
        public IActionResult UpdateContract(string name, [FromBody] Contract updatedContract)
        {
            _logger.LogInformation($"Update Contract request initiated for name: {SanitizeForLog(name)}");
            if (updatedContract == null)
            {
                _logger.LogWarning("Invalid contract data");
                return BadRequest("Invalid contract data.");
            }

            try
            {
                _contractService.UpdateContract(name, updatedContract);
                return Ok("Contract has been updated successfully.");
            }
            catch (DataNotFoundException ex)
            {
                _logger.LogWarning($"Contract with name {SanitizeForLog(name)} not found");
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError("An error occurred while updating the contract : {ExceptionMessage}", ex);
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        [HttpDelete("{name}")]
        public IActionResult DeleteContract(string name)
        {
            _logger.LogWarning($"Contract with name {SanitizeForLog(name)} not found");
            try
            {
                _contractService.DeleteContract(name);
                return Ok("Contract has been deleted successfully.");
            }
            catch (DataNotFoundException ex)
            {
                _logger.LogWarning($"Contract with name {SanitizeForLog(name)} not found");
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError("An error occurred while deleting the contract : {ExceptionMessage}", ex);
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}