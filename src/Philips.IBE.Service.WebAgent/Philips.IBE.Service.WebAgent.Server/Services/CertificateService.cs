
using Philips.IBE.Service.WebAgent.Server.Configuration;

namespace Philips.IBE.Service.WebAgent.Server.Services
{
    public class CertificateService : ICertificateService
    {
        private readonly string _certificateFolder;
        private readonly ILogger<CertificateService> _logger;

        private string SanitizeForLog(string input)
        {
            if (input == null)
                return string.Empty;
            return input.Replace("\r", "").Replace("\n", "");
        }

        public CertificateService(AppConfiguration configuration, ILogger<CertificateService> logger)
        {

            _certificateFolder = Path.Combine(configuration.CommonConfiguration.FolderPath, configuration.CommonConfiguration.CertificateFolderName);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            EnsureBaseDirectoryExists();
        }

        private void EnsureBaseDirectoryExists()
        {
            if (!Directory.Exists(_certificateFolder))
            {
                Directory.CreateDirectory(_certificateFolder);
                _logger.LogInformation("Base certificate folder created successfully at {Path}", _certificateFolder);
            }
        }

        public async Task<bool> Upload2FilesAsync(IFormFile file1, IFormFile file2, string folderName)
        {
            var safeFolderName = folderName?.Replace("\r", "").Replace("\n", "");
            try
            {
                var folderPath = Path.Combine(_certificateFolder, folderName);
                Directory.CreateDirectory(folderPath);
                _logger.LogInformation("Created folder {FolderName} at {Path}", safeFolderName, SanitizeForLog(folderPath));

                await SaveFileAsync(file1, folderPath);
                await SaveFileAsync(file2, folderPath);

                _logger.LogInformation("Files uploaded successfully to {FolderName}", safeFolderName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading files to {FolderName}", safeFolderName);
                return false;
            }
        }

        public async Task<bool> UploadFileAsync(IFormFile file1, string folderName)
        {
            var safeFolderName = folderName?.Replace("\r", "").Replace("\n", "");
            try
            {
                var folderPath = Path.Combine(_certificateFolder, folderName);
                Directory.CreateDirectory(folderPath);
                _logger.LogInformation("Created folder {FolderName} at {Path}", safeFolderName, SanitizeForLog(folderPath));


                await SaveFileAsync(file1, folderPath);

                _logger.LogInformation("Files uploaded successfully to {FolderName}", safeFolderName);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading files to {FolderName}", safeFolderName);
                return false;
            }
        }

        private async Task SaveFileAsync(IFormFile file, string folderPath)
        {
            if (file != null && file.Length > 0)
            {
                var filePath = Path.Combine(folderPath, file.FileName);
                var safeFileName = SanitizeForLog(file.FileName);
                var safeFilePath = SanitizeForLog(filePath);
                _logger.LogInformation("Saving file {FileName} to {Path}", safeFileName, safeFilePath);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }
                _logger.LogInformation("File {FileName} saved successfully", safeFileName);
            }
        }

        public bool DeleteFolder(string folderName)
        {
            var safeFolderName = folderName?.Replace("\r", "").Replace("\n", "");
            try
            {
                var folderPath = Path.Combine(_certificateFolder, folderName);
                if (Directory.Exists(folderPath))
                {
                    Directory.Delete(folderPath, true);
                    _logger.LogInformation("Folder {FolderName} deleted successfully", safeFolderName);
                    return true;
                }
                _logger.LogWarning("Folder {FolderName} does not exist", safeFolderName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting folder {FolderName}", safeFolderName);
                return false;
            }
        }

        public bool DeleteFile(string folderName, string fileName)
        {
            var safeFileName = fileName?.Replace("\r", "").Replace("\n", "");
            try
            {
                var FilePath = Path.Combine(_certificateFolder, folderName, fileName);
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                    _logger.LogInformation("File {fileName} deleted successfully", safeFileName);
                    return true;
                }
                _logger.LogWarning("File {fileName} does not exist", safeFileName);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting folder {fileName}", safeFileName);
                return false;
            }
        }
    }
}
