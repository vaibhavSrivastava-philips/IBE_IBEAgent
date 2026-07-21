using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Philips.IBE.Service.WebAgent.Server.Services;

namespace Philips.IBE.Service.WebAgent.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "admin")]
    public class CertificateController : ControllerBase
    {
        private readonly ICertificateService _certificateService;
        private readonly ILogger<CertificateController> _logger;

        private string SanitizeForLog(string input)
        {
            if (input == null)
                return string.Empty;
            return input.Replace("\r", "").Replace("\n", "");
        }

        public CertificateController(ICertificateService certificateService, ILogger<CertificateController> logger)
        {
            _certificateService = certificateService;
            _logger = logger;
        }

        [HttpPost("multiple")]
        public async Task<IActionResult> UploadFiles(IFormFile file1, IFormFile file2, [FromQuery] string folderName)
        {
            _logger.LogInformation("Certificate upload request initiated");
            if (string.IsNullOrWhiteSpace(folderName))
            {
                _logger.LogError("provided folder name is incorrect");
                return BadRequest("Folder name is required.");
            }

            var result = await _certificateService.Upload2FilesAsync(file1, file2, folderName);

            if (result)
            {
                _logger.LogInformation("Files uploaded successfully");
                return Ok(new { message = "Files uploaded successfully" });
            }
            else
            {
                var safeFolderName = SanitizeForLog(folderName);
                _logger.LogInformation($"Failed to upload file [{safeFolderName}]");
                return StatusCode(500, new { message = "An error occurred while uploading files" });
            }
        }

        [HttpPost("single")]
        public async Task<IActionResult> UploadFiles(IFormFile file1, [FromQuery] string folderName)
        {
            _logger.LogInformation("Certificate upload request initiated");
            if (string.IsNullOrWhiteSpace(folderName))
            {
                _logger.LogError("provided folder name is incorrect");
                return BadRequest("Folder name is required.");
            }

            var result = await _certificateService.UploadFileAsync(file1, folderName);

            if (result)
            {
                _logger.LogInformation("Files uploaded successfully");
                return Ok(new { message = "Files uploaded successfully" });
            }
            else
            {
                var safeFolderName = SanitizeForLog(folderName);
                _logger.LogInformation($"Failed to upload file [{safeFolderName}]");
                return StatusCode(500, new { message = "An error occurred while uploading files" });
            }
        }

        [HttpDelete("folder")]
        public IActionResult DeleteFolder([FromQuery] string folderName)
        {
            _logger.LogInformation("Certificate delete request initiated");
            if (string.IsNullOrWhiteSpace(folderName))
            {
                _logger.LogError("provided folder name is incorrect");
                return BadRequest("Folder name is required.");
            }

            var result = _certificateService.DeleteFolder(folderName);

            if (result)
            {
                var safeFolderName = SanitizeForLog(folderName);
                _logger.LogInformation($"{safeFolderName} deleted");
                return Ok(new { message = "Folder deleted successfully" });


            }
            else
            {
                _logger.LogInformation("Folder not found or an error occurred");
                return NotFound(new { message = "Folder not found or an error occurred" });
            }
        }

        [HttpDelete("file")]
        public IActionResult DeleteFile([FromQuery] string folderName, [FromQuery] string fileName)
        {
            _logger.LogInformation("Certificate delete request initiated");
            if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(fileName))
            {
                _logger.LogError("provided folder name or file name is incorrect");
                return BadRequest("Folder name and file name are required.");
            }

            var result = _certificateService.DeleteFile(folderName, fileName);

            if (result)
            {
                var safeFileName = SanitizeForLog(fileName);
                var safeFolderName = SanitizeForLog(folderName);
                _logger.LogInformation($"{safeFileName} deleted from {safeFolderName}");
                return Ok(new { message = "File deleted successfully" });
            }
            else
            {
                _logger.LogInformation("File not found or an error occurred");
                return NotFound(new { message = "File not found or an error occurred" });
            }
        }
    }
}