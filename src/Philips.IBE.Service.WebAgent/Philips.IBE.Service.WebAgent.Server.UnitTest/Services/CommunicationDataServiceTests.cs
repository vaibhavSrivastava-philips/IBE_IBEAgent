using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Philips.IBE.Service.WebAgent.Server.Configuration;
using Philips.IBE.Service.WebAgent.Server.Exceptions;
using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Services;
using Philips.IBE.Service.WebAgent.Server.Utilities;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Services
{

    public class CommunicationDataServiceTests : IDisposable
    {
        private readonly string _baseFolder;
        private readonly string _certFolder;
        private readonly string _dataFile;
        private readonly AppConfiguration _appConfig;
        private readonly Mock<DataProtectionUtility> _protectionMock;
        private readonly Mock<ILogger<CommunicationDataService>> _loggerMockForService; 
        private readonly Mock<ILogger<DataProtectionUtility>> _loggerMockForUtility; 
        public CommunicationDataServiceTests()
        {
            _baseFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            _certFolder = Path.Combine(_baseFolder, "certs");
            Directory.CreateDirectory(_certFolder);
            _dataFile = Path.Combine(_baseFolder, "communicationData.json");
            var inMemorySettings = new Dictionary<string, string>
        {
            {"CommonConfiguration:FolderPath", _baseFolder},
            {"CommonConfiguration:CertificateFolderName", "certs"},
            {"CommonConfiguration:DatabaseEnabled", "false"},
            {"CommonConfiguration:DatabaseFileName", "test.db"},
            {"CommonConfiguration:ServiceConfigPath", "servicesettings.json"},
            {"CommonConfiguration:License", "test-license"},
            {"AuthenticationConfiguration:AuthenticationMode", "Test"},
            {"AuthenticationConfiguration:AdminUserGroup", "Admin"},
            {"AuthenticationConfiguration:NormalUserGroup", "User"},
            {"JwtOptions:Issuer", "issuer"},
            {"JwtOptions:Audience", "audience"},
            {"JwtOptions:ExpirationSeconds", "3600"}
        };
            var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();
            _appConfig = new AppConfiguration(configuration);
            _loggerMockForUtility = new Mock<ILogger<DataProtectionUtility>>();
            _protectionMock = new Mock<DataProtectionUtility>();
            _protectionMock.Setup(p => p.ProtectValue(It.IsAny<string>())).Returns((string s) => $"protected-{s}");
            _loggerMockForService = new Mock<ILogger<CommunicationDataService>>();
        }

        [Fact]
        public void Constructor_Creates_Data_File_If_Not_Exists()
        {
            if (File.Exists(_dataFile))
                File.Delete(_dataFile);
            new CommunicationDataService(_appConfig, _protectionMock.Object, _loggerMockForService.Object);
            Assert.True(File.Exists(_dataFile));
        }

        [Fact]
        public void GetAllCommunicationData_Returns_Empty_If_File_Empty()
        {
            File.WriteAllText(_dataFile, "");
            var service = new CommunicationDataService(_appConfig, _protectionMock.Object, _loggerMockForService.Object); // Use correct logger
            var result = service.GetAllCommunicationData();
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void AddCommunicationData_Adds_And_Persists_Data()
        {
            var service = new CommunicationDataService(_appConfig, _protectionMock.Object, _loggerMockForService.Object);

            var point = new CommunicationPoint
            {
                Name = "Test",
                Type = "TCP",
                Mode = "Active",
                IsSSLEnabled = true,
                CertificateDetails = new CertificateConfigurations 
                {

                    ClientCertificatePassword = "dummyPasswordToProtect"


                }
            };

            service.AddCommunicationData(point);

            var all = service.GetAllCommunicationData();
            Assert.Single(all);
            Assert.Equal("Test", all[0].Name);

            Assert.NotNull(all[0].CertificateDetails); 
            Assert.StartsWith("protected-", all[0].CertificateDetails.ClientCertificatePassword);
        }
        [Fact]
        public void GetCommunicationDataById_Returns_Correct_Item()
        {
            var service = new CommunicationDataService(_appConfig, _protectionMock.Object, _loggerMockForService.Object);

            var point = new CommunicationPoint
            {
                Name = "Test2",
                Type = "TCP",
                Mode = "Active"
            };
            service.AddCommunicationData(point);

            var all = service.GetAllCommunicationData();
            var id = all[0].Id;

            var result = service.GetCommunicationDataById(id);

            Assert.NotNull(result);
            Assert.Equal("Test2", result.Name);
        }

        [Fact]
        public void UpdateCommunicationData_Updates_Existing()
        {
            var service = new CommunicationDataService(_appConfig, _protectionMock.Object, _loggerMockForService.Object);

            var point = new CommunicationPoint
            {
                Name = "Test3",
                Type = "TCP",
                Mode = "Active",
                IsSSLEnabled = false
            };
            service.AddCommunicationData(point);

            var all = service.GetAllCommunicationData();
            var id = all[0].Id;

            var updated = new CommunicationPoint
            {
                Name = "Updated",
                Type = "HTTP",
                Mode = "Passive",
                IsSSLEnabled = false
            };

            service.UpdateCommunicationData(id, updated);

            var result = service.GetCommunicationDataById(id);
            Assert.NotNull(result);
            Assert.Equal("Updated", result.Name);
            Assert.Equal("http", result.Type);
            Assert.Equal("passive", result.Mode);
        }

        [Fact]
        public void UpdateCommunicationData_Throws_If_Not_Found()
        {
            var service = new CommunicationDataService(_appConfig, _protectionMock.Object, _loggerMockForService.Object);

            var updated = new CommunicationPoint
            {
                Name = "Updated",
                Type = "HTTP",
                Mode = "Passive",
                IsSSLEnabled = false
            };

            Assert.Throws<DataNotFoundException>(() => service.UpdateCommunicationData(999, updated));
        }

        [Fact]
        public void DeleteCommunicationData_Removes_Item()
        {
            var service = new CommunicationDataService(_appConfig, _protectionMock.Object, _loggerMockForService.Object);

            var point = new CommunicationPoint
            {
                Name = "Test4",
                Type = "TCP",
                Mode = "Active"
            };
            service.AddCommunicationData(point);

            var all = service.GetAllCommunicationData();
            var id = all[0].Id;

            service.DeleteCommunicationData(id);

            var afterDelete = service.GetAllCommunicationData();
            Assert.Empty(afterDelete);
        }

        [Fact]
        public void DeleteCommunicationData_Throws_If_Not_Found()
        {
            var service = new CommunicationDataService(_appConfig, _protectionMock.Object, _loggerMockForService.Object);

            Assert.Throws<DataNotFoundException>(() => service.DeleteCommunicationData(999));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_baseFolder))
                    Directory.Delete(_baseFolder, true);
            }
            catch { }
        }
    }
}
