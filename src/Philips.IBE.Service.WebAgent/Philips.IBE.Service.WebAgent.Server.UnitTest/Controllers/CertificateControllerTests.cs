using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Philips.IBE.Service.WebAgent.Server.Controllers;
using Philips.IBE.Service.WebAgent.Server.Services;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Controllers
{
    public class CertificateControllerTests
    {
        private readonly Mock<ICertificateService> _serviceMock;
        private readonly Mock<ILogger<CertificateController>> _loggerMock;
        private readonly CertificateController _controller;

        public CertificateControllerTests()
        {
            _serviceMock = new Mock<ICertificateService>();
            _loggerMock = new Mock<ILogger<CertificateController>>();
            _controller = new CertificateController(_serviceMock.Object, _loggerMock.Object);
        }

        [Fact]
        public async Task UploadFiles_Multiple_ReturnsOk_WhenSuccess()
        {
            // Arrange
            var file1 = new Mock<IFormFile>().Object;
            var file2 = new Mock<IFormFile>().Object;
            _serviceMock.Setup(s => s.Upload2FilesAsync(file1, file2, "folder")).ReturnsAsync(true);

            // Act
            var result = await _controller.UploadFiles(file1, file2, "folder");

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, okResult.StatusCode);
        }

        [Fact]
        public async Task UploadFiles_Multiple_ReturnsBadRequest_WhenFolderNameMissing()
        {
            var file1 = new Mock<IFormFile>().Object;
            var file2 = new Mock<IFormFile>().Object;

            var result = await _controller.UploadFiles(file1, file2, null);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Folder name is required.", badRequest.Value);
        }

        [Fact]
        public async Task UploadFiles_Multiple_ReturnsServerError_WhenServiceFails()
        {
            var file1 = new Mock<IFormFile>().Object;
            var file2 = new Mock<IFormFile>().Object;
            _serviceMock.Setup(s => s.Upload2FilesAsync(file1, file2, "folder")).ReturnsAsync(false);

            var result = await _controller.UploadFiles(file1, file2, "folder");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        [Fact]
        public async Task UploadFiles_Single_ReturnsOk_WhenSuccess()
        {
            var file1 = new Mock<IFormFile>().Object;
            _serviceMock.Setup(s => s.UploadFileAsync(file1, "folder")).ReturnsAsync(true);

            var result = await _controller.UploadFiles(file1, "folder");

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, okResult.StatusCode);
        }

        [Fact]
        public async Task UploadFiles_Single_ReturnsBadRequest_WhenFolderNameMissing()
        {
            var file1 = new Mock<IFormFile>().Object;

            var result = await _controller.UploadFiles(file1, null);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Folder name is required.", badRequest.Value);
        }

        [Fact]
        public async Task UploadFiles_Single_ReturnsServerError_WhenServiceFails()
        {
            var file1 = new Mock<IFormFile>().Object;
            _serviceMock.Setup(s => s.UploadFileAsync(file1, "folder")).ReturnsAsync(false);

            var result = await _controller.UploadFiles(file1, "folder");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        [Fact]
        public void DeleteFolder_ReturnsOk_WhenSuccess()
        {
            _serviceMock.Setup(s => s.DeleteFolder("folder")).Returns(true);

            var result = _controller.DeleteFolder("folder");

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, okResult.StatusCode);
        }

        [Fact]
        public void DeleteFolder_ReturnsBadRequest_WhenFolderNameMissing()
        {
            var result = _controller.DeleteFolder(null);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Folder name is required.", badRequest.Value);
        }

        [Fact]
        public void DeleteFolder_ReturnsNotFound_WhenServiceFails()
        {
            _serviceMock.Setup(s => s.DeleteFolder("folder")).Returns(false);

            var result = _controller.DeleteFolder("folder");

            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal(404, notFound.StatusCode);
        }

        [Fact]
        public void DeleteFile_ReturnsOk_WhenSuccess()
        {
            _serviceMock.Setup(s => s.DeleteFile("folder", "file.txt")).Returns(true);

            var result = _controller.DeleteFile("folder", "file.txt");

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, okResult.StatusCode);
        }

        [Fact]
        public void DeleteFile_ReturnsBadRequest_WhenFolderOrFileNameMissing()
        {
            var result1 = _controller.DeleteFile(null, "file.txt");
            var result2 = _controller.DeleteFile("folder", null);

            Assert.IsType<BadRequestObjectResult>(result1);
            Assert.IsType<BadRequestObjectResult>(result2);
        }

        [Fact]
        public void DeleteFile_ReturnsNotFound_WhenServiceFails()
        {
            _serviceMock.Setup(s => s.DeleteFile("folder", "file.txt")).Returns(false);

            var result = _controller.DeleteFile("folder", "file.txt");

            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal(404, notFound.StatusCode);
        }
    }
}
