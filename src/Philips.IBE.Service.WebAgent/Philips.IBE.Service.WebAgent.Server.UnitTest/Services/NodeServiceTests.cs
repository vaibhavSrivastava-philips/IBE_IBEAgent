using System;
using System.IO;
using System.Runtime.Versioning;
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
    [SupportedOSPlatform("windows")]
    public class NodeServiceTests : IDisposable
    {
        private readonly string _tempFolder;
        private readonly AppConfiguration _appConfig;
        private readonly Mock<ILogger<NodeService>> _loggerMock;
        private readonly string _configPath;
        private Mock<DataProtectionUtility> _protectionMock;
        private Mock<ILogger<DataProtectionUtility>> _mockDataProtectionLogger; 

        public NodeServiceTests()
        {
            _tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempFolder);

            var inMemorySettings = new Dictionary<string, string?>
            {
                { "CommonConfiguration:FolderPath", _tempFolder },
                { "CommonConfiguration:CertificateFolderName", "certs" },
                { "CommonConfiguration:DatabaseFileName", "testdb" },
                { "CommonConfiguration:DatabaseEnabled", "true" },
                { "CommonConfiguration:ServiceConfigPath", _tempFolder },
                { "CommonConfiguration:License", "test-license" },
                { "AuthenticationConfiguration:AdminUserGroup", "AdminGroup" },
                { "AuthenticationConfiguration:NormalUserGroup", "UserGroup" },
                { "AuthenticationConfiguration:AuthenticationMode", "ActiveDirectory" },
                { "JwtOptions:Issuer", "TestIssuer" },
                { "JwtOptions:Audience", "TestAudience" },
                { "JwtOptions:Key", "TestKeyTestKeyTestKeyTestKey" },
                { "JwtOptions:ExpirationSeconds", "3600" }
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();
            _appConfig = new AppConfiguration(configuration);

            _loggerMock = new Mock<ILogger<NodeService>>();
            _mockDataProtectionLogger = new Mock<ILogger<DataProtectionUtility>>();
            _protectionMock = new Mock<DataProtectionUtility>(); _protectionMock.Setup(p => p.ProtectValue(It.IsAny<string>())).Returns((string s) => $"protected-{s}");

            _configPath = Path.Combine(_tempFolder, "appsettings.json");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempFolder))
                Directory.Delete(_tempFolder, true);
        }

        [Fact]
        public void Constructor_Throws_WhenCommonConfigurationIsNull()
        {
            var configuration = new ConfigurationBuilder().Build();
            Assert.Throws<DataNotFoundException>(() =>
                new NodeService(new AppConfiguration(configuration), _loggerMock.Object, _protectionMock.Object));
        }

        [Fact]
        public void Constructor_Throws_WhenLoggerIsNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new NodeService(_appConfig, null!, _protectionMock.Object));
        }

        [Fact]
        public void GetServiceNode_Throws_WhenNodesIsNull()
        {
            var configs = new ServiceConfigurationRoot { ServiceConfigurations = new ServiceConfigurations { Nodes = null } };
            File.WriteAllText(_configPath, Newtonsoft.Json.JsonConvert.SerializeObject(configs));
            var service = new NodeService(_appConfig, _loggerMock.Object, _protectionMock.Object);

            Assert.Throws<InvalidOperationException>(() => service.GetServiceNode());
        }

        [Fact]
        public void GetServiceNode_Returns_Nodes()
        {
            var node = new ServiceNode();
            var configs = new ServiceConfigurationRoot { ServiceConfigurations = new ServiceConfigurations { Nodes = node } };
            File.WriteAllText(_configPath, Newtonsoft.Json.JsonConvert.SerializeObject(configs));
            var service = new NodeService(_appConfig, _loggerMock.Object, _protectionMock.Object);

            var result = service.GetServiceNode();

            Assert.NotNull(result);
        }

        [Fact]
        public void UpdateHTTPServiceNode_Updates_And_Saves()
        {
            var configs = new ServiceConfigurationRoot { ServiceConfigurations = new ServiceConfigurations { Nodes = new ServiceNode() } };
            File.WriteAllText(_configPath, Newtonsoft.Json.JsonConvert.SerializeObject(configs));
            var service = new NodeService(_appConfig, _loggerMock.Object, _protectionMock.Object);
            var nodeConfig = new ServiceNodeConfiguration { Port = 1234 };

            var result = service.UpdateHTTPServiceNode(nodeConfig);

            Assert.True(result);
        }

        [Fact]
        public void UpdateTCPServiceNode_ProtectsPasswords_And_Saves()
        {
            var configs = new ServiceConfigurationRoot { ServiceConfigurations = new ServiceConfigurations { Nodes = new ServiceNode() } };
            File.WriteAllText(_configPath, Newtonsoft.Json.JsonConvert.SerializeObject(configs));
            var service = new NodeService(_appConfig, _loggerMock.Object, _protectionMock.Object);
            var sslConfig = new CertificateConfigurations
            {
                ServerCertificatePassword = "serverpass",
                ClientCertificatePassword = "clientpass",
                ServerCertificatePath = "server.pfx",
                ClientCertificatePath = "client.pfx"
            };
            var nodeConfig = new ServiceNodeConfiguration
            {
                EnableSSL = true,
                SSLConfiguration = sslConfig
            };

            var result = service.UpdateTCPServiceNode(nodeConfig);

            Assert.True(result);
            Assert.StartsWith("protected-", nodeConfig.SSLConfiguration.ServerCertificatePassword);
            Assert.StartsWith("protected-", nodeConfig.SSLConfiguration.ClientCertificatePassword);
            Assert.Contains("tcp-service", nodeConfig.SSLConfiguration.ServerCertificatePath);
            Assert.Contains("tcp-service", nodeConfig.SSLConfiguration.ClientCertificatePath);
        }

        [Fact]
        public void UpdateADTServiceNode_ProtectsPasswords_And_Saves()
        {
            var configs = new ServiceConfigurationRoot { ServiceConfigurations = new ServiceConfigurations { Nodes = new ServiceNode() } };
            File.WriteAllText(_configPath, Newtonsoft.Json.JsonConvert.SerializeObject(configs));
            var service = new NodeService(_appConfig, _loggerMock.Object, _protectionMock.Object);
            var sslConfig = new CertificateConfigurations
            {
                ServerCertificatePassword = "serverpass",
                ClientCertificatePassword = "clientpass",
                ServerCertificatePath = "server.pfx",
                ClientCertificatePath = "client.pfx"
            };
            var nodeConfig = new ServiceNodeConfiguration
            {
                EnableSSL = true,
                SSLConfiguration = sslConfig
            };

            var result = service.UpdateADTServiceNode(nodeConfig);

            Assert.True(result);
            Assert.StartsWith("protected-", nodeConfig.SSLConfiguration.ServerCertificatePassword);
            Assert.StartsWith("protected-", nodeConfig.SSLConfiguration.ClientCertificatePassword);
            Assert.Contains("adt-service", nodeConfig.SSLConfiguration.ServerCertificatePath);
            Assert.Contains("adt-service", nodeConfig.SSLConfiguration.ClientCertificatePath);
        }

        [Fact]
        public void UpdateWebSocketClientServiceNode_ProtectsPassword_And_Saves()
        {
            var configs = new ServiceConfigurationRoot { ServiceConfigurations = new ServiceConfigurations { Nodes = new ServiceNode() } };
            File.WriteAllText(_configPath, Newtonsoft.Json.JsonConvert.SerializeObject(configs));
            var service = new NodeService(_appConfig, _loggerMock.Object, _protectionMock.Object);
            var sslConfig = new CertificateConfigurations
            {
                ServerCertificatePassword = "serverpass",
                ServerCertificatePath = "server.pfx",
                RootCertificatePath = "root.pfx"
            };
            var nodeConfig = new ServiceNodeConfiguration
            {
                EnableSSL = true,
                SSLConfiguration = sslConfig
            };

            var result = service.UpdateWebSocketClientServiceNode(nodeConfig);

            Assert.True(result);
            Assert.StartsWith("protected-", nodeConfig.SSLConfiguration.ServerCertificatePassword);
            Assert.Contains("webSocket-service", nodeConfig.SSLConfiguration.ServerCertificatePath);
            Assert.Contains("webSocket-service", nodeConfig.SSLConfiguration.RootCertificatePath);
        }
    }
}

