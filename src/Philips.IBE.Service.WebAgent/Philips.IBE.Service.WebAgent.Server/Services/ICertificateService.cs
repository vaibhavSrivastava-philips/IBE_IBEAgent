namespace Philips.IBE.Service.WebAgent.Server.Services
{
    public interface ICertificateService
    {
        Task<bool> Upload2FilesAsync(IFormFile file1, IFormFile file2, string folderName);
        Task<bool> UploadFileAsync(IFormFile file1,string folderName);
        bool DeleteFolder(string folderName);
        bool DeleteFile(string folderName, string fileName);
    }
}
