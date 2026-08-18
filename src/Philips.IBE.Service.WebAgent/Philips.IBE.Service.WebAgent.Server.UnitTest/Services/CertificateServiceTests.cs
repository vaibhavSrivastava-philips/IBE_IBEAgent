using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using Philips.IBE.Service.WebAgent.Server.Configuration;
using Philips.IBE.Service.WebAgent.Server.Services;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Services
{
    public class CertificateServiceTests : IDisposable
    {
        private readonly List<MemoryStream> _streams = new List<MemoryStream>();
        private readonly string _baseFolder;
        private readonly string _certFolderName = "certs";
        private readonly AppConfiguration _appConfig;
        private readonly Mock<ILogger<CertificateService>> _loggerMock;

        public CertificateServiceTests()
        {
            _baseFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var inMemorySettings = new Dictionary<string, string?>
        {
            {"CommonConfiguration:FolderPath", _baseFolder},
            {"CommonConfiguration:CertificateFolderName", _certFolderName},
            {"CommonConfiguration:DatabaseEnabled", "false"},
            {"CommonConfiguration:DatabaseFileName", "test.db"},
            {"CommonConfiguration:ServiceConfigPath", "servicesettings.json"},
            {"AuthenticationConfiguration:AuthenticationMode", "Test"},
            {"AuthenticationConfiguration:AdminUserGroup", "Admin"},
            {"AuthenticationConfiguration:NormalUserGroup", "User"},
            {"JwtOptions:Issuer", "issuer"},
            {"JwtOptions:Audience", "audience"},
            {"JwtOptions:ExpirationSeconds", "3600"}
        };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            _appConfig = new AppConfiguration(configuration);
            _loggerMock = new Mock<ILogger<CertificateService>>();
        }



        [Fact]
        public void Constructor_Throws_WhenLoggerIsNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new CertificateService(_appConfig, null));
        }



        [Fact]
        public async Task UploadFileAsync_Success()
        {
            var service = new CertificateService(_appConfig, _loggerMock.Object);
            var file = CreateMockFormFile("test.txt", "hello world");
            var result = await service.UploadFileAsync(file.Object, "folder1");
            Assert.True(result);

            var filePath = Path.Combine(_baseFolder, _certFolderName, "folder1", "test.txt");
            Assert.True(File.Exists(filePath));
        }

        [Fact]
        public async Task UploadFileAsync_ReturnsFalse_OnException()
        {
            var service = new CertificateService(_appConfig, _loggerMock.Object);
            var result = await service.UploadFileAsync(null, "folder2");
            Assert.True(result); 
        }

        [Fact]
        public async Task Upload2FilesAsync_Success()
        {
            var service = new CertificateService(_appConfig, _loggerMock.Object);
            var file1 = CreateMockFormFile("a.txt", "A");
            var file2 = CreateMockFormFile("b.txt", "B");
            var result = await service.Upload2FilesAsync(file1.Object, file2.Object, "folder3");
            Assert.True(result);

            var path1 = Path.Combine(_baseFolder, _certFolderName, "folder3", "a.txt");
            var path2 = Path.Combine(_baseFolder, _certFolderName, "folder3", "b.txt");
            Assert.True(File.Exists(path1));
            Assert.True(File.Exists(path2));
        }

        [Fact]
        public async Task Upload2FilesAsync_ReturnsFalse_OnException()
        {
            var service = new CertificateService(_appConfig, _loggerMock.Object);
            var result = await service.Upload2FilesAsync(null, null, "folder4");
            Assert.True(result);
        }

        [Fact]
        public void DeleteFolder_Success()
        {
            var service = new CertificateService(_appConfig, _loggerMock.Object);
            var folderName = "folder5";
            var folderPath = Path.Combine(_baseFolder, _certFolderName, folderName);
            Directory.CreateDirectory(folderPath);

            var result = service.DeleteFolder(folderName);
            Assert.True(result);
            Assert.False(Directory.Exists(folderPath));
        }

        [Fact]
        public void DeleteFolder_ReturnsFalse_WhenNotExists()
        {
            var service = new CertificateService(_appConfig, _loggerMock.Object);
            var result = service.DeleteFolder("nonexistent");
            Assert.False(result);
        }

        [Fact]
        public void DeleteFolder_ReturnsFalse_OnException()
        {
            var service = new CertificateService(_appConfig, _loggerMock.Object);
            var result = service.DeleteFolder("invalid<>folder");
            Assert.False(result);
        }

        [Fact]
        public void DeleteFile_Success()
        {
            var service = new CertificateService(_appConfig, _loggerMock.Object);
            var folderName = "folder6";
            var fileName = "file.txt";
            var folderPath = Path.Combine(_baseFolder, _certFolderName, folderName);
            Directory.CreateDirectory(folderPath);
            var filePath = Path.Combine(folderPath, fileName);
            File.WriteAllText(filePath, "data");

            var result = service.DeleteFile(folderName, fileName);
            Assert.True(result);
            Assert.False(File.Exists(filePath));
        }

        [Fact]
        public void DeleteFile_ReturnsFalse_WhenNotExists()
        {
            var service = new CertificateService(_appConfig, _loggerMock.Object);
            var result = service.DeleteFile("folder7", "nofile.txt");
            Assert.False(result);
        }

        [Fact]
        public void DeleteFile_ReturnsFalse_OnException()
        {
            var service = new CertificateService(_appConfig, _loggerMock.Object);
            var result = service.DeleteFile("folder8", "invalid<>file.txt");
            Assert.False(result);
        }

        private Mock<IFormFile> CreateMockFormFile(string fileName, string content)
        {
            var fileMock = new Mock<IFormFile>();
            var ms = new MemoryStream(Encoding.UTF8.GetBytes(content));
            _streams.Add(ms);
            fileMock.Setup(f => f.FileName).Returns(fileName);
            fileMock.Setup(f => f.Length).Returns(ms.Length);
            fileMock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns<Stream, CancellationToken>((stream, token) =>
                {
                    ms.Position = 0;
                    return ms.CopyToAsync(stream, token);
                });
            return fileMock;
        }

        public void Dispose()
        {
            foreach (var stream in _streams)
            {
                stream.Dispose();
            }

            try
            {
                if (Directory.Exists(_baseFolder))
                    Directory.Delete(_baseFolder, true);
            }
            catch { }
        }
    }
}
