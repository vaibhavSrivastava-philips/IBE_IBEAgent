using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Philips.IBE.Service.WebAgent.Server.Configuration;
using Philips.IBE.Service.WebAgent.Server.Exceptions;
using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Services;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Services
{
    public class ContractServiceTests : IDisposable
    {
        private readonly string _testFolder;
        private readonly AppConfiguration _config;
        private readonly Mock<ILogger<ContractService>> _loggerMock;

        public ContractServiceTests()
        {
            _testFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testFolder);
            var inMemorySettings = new Dictionary<string, string>
    {
        {"AuthenticationConfiguration:AuthenticationMode", "TestMode"},
        {"AuthenticationConfiguration:AdminUserGroup", "AdminGroup"},
        {"AuthenticationConfiguration:NormalUserGroup", "UserGroup"},
        {"JwtOptions:Issuer", "TestIssuer"},
        {"JwtOptions:Audience", "TestAudience"},
        {"JwtOptions:ExpirationSeconds", "3600"},
        {"CommonConfiguration:FolderPath", _testFolder},
        {"CommonConfiguration:DatabaseEnabled", "false"},
        {"CommonConfiguration:CertificateFolderName", "certs"},
        {"CommonConfiguration:DatabaseFileName", "testdb.json"}
    };
            var configuration = new ConfigurationBuilder()
.AddInMemoryCollection(inMemorySettings.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)))
                .Build();

            _config = new AppConfiguration(configuration);

            _loggerMock = new Mock<ILogger<ContractService>>();
        }

        public void Dispose()
        {
            if (Directory.Exists(_testFolder))
                Directory.Delete(_testFolder, true);
        }

        [Fact]
        public void GetAllContracts_ReturnsEmptyList_WhenNoContracts()
        {
            var service = new ContractService(_config, _loggerMock.Object);
            var result = service.GetAllContracts();
            Assert.Empty(result);
        }

        [Fact]
        public void AddContract_AddsContractSuccessfully()
        {
            var service = new ContractService(_config, _loggerMock.Object);
            var contract = new Contract { Name = "TestContract" };
            service.AddContract(contract);

            var contracts = service.GetAllContracts();
            Assert.Single(contracts);
            Assert.Equal("TestContract", contracts[0].Name);
        }

        [Fact]
        public void AddContract_Throws_WhenContractIsNull()
        {
            var service = new ContractService(_config, _loggerMock.Object);
            Assert.Throws<ArgumentNullException>(() => service.AddContract(null));
        }

        [Fact]
        public void AddContract_Throws_WhenDuplicateName()
        {
            var service = new ContractService(_config, _loggerMock.Object);
            var contract = new Contract { Name = "Duplicate" };
            service.AddContract(contract);

            var duplicate = new Contract { Name = "Duplicate" };
            Assert.Throws<InvalidOperationException>(() => service.AddContract(duplicate));
        }

        [Fact]
        public void GetAllContracts_ReturnsAllContracts()
        {
            var service = new ContractService(_config, _loggerMock.Object);
            service.AddContract(new Contract { Name = "A" });
            service.AddContract(new Contract { Name = "B" });

            var contracts = service.GetAllContracts();
            Assert.Equal(2, contracts.Count);
        }

        [Fact]
        public void UpdateContract_UpdatesExistingContract()
        {
            var service = new ContractService(_config, _loggerMock.Object);
            var contract = new Contract { Name = "OldName" };
            service.AddContract(contract);

            var updated = new Contract { Name = "NewName" };
            service.UpdateContract("OldName", updated);

            var contracts = service.GetAllContracts();
            Assert.Single(contracts);
            Assert.Equal("NewName", contracts[0].Name);
        }

        [Fact]
        public void UpdateContract_Throws_WhenNotFound()
        {
            var service = new ContractService(_config, _loggerMock.Object);
            var updated = new Contract { Name = "DoesNotExist" };
            Assert.Throws<DataNotFoundException>(() => service.UpdateContract("Missing", updated));
        }

        [Fact]
        public void UpdateContract_Throws_WhenUpdatedContractIsNull()
        {
            var service = new ContractService(_config, _loggerMock.Object);
            service.AddContract(new Contract { Name = "A" });
            Assert.Throws<ArgumentNullException>(() => service.UpdateContract("A", null));
        }

        [Fact]
        public void DeleteContract_RemovesContract()
        {
            var service = new ContractService(_config, _loggerMock.Object);
            var contract = new Contract { Name = "ToDelete" };
            service.AddContract(contract);

            service.DeleteContract("ToDelete");
            Assert.Empty(service.GetAllContracts());
        }

        [Fact]
        public void DeleteContract_Throws_WhenNotFound()
        {
            var service = new ContractService(_config, _loggerMock.Object);
            Assert.Throws<DataNotFoundException>(() => service.DeleteContract("Missing"));
        }

        [Fact]
        public void DeleteContract_Throws_WhenNameIsNull()
        {
            var service = new ContractService(_config, _loggerMock.Object);
            Assert.Throws<ArgumentNullException>(() => service.DeleteContract(null));
        }

        [Fact]
        public void AddContract_Throws_WhenNameIsNull()
        {
            var service = new ContractService(_config, _loggerMock.Object);
            var contract = new Contract { Name = string.Empty };
            Assert.Throws<ArgumentException>(() => service.AddContract(contract));
        }

        [Fact]
        public void UpdateContract_Throws_WhenNameIsNull()
        {
            var service = new ContractService(_config, _loggerMock.Object);
            var contract = new Contract { Name = "A" };
            service.AddContract(contract);
            var updated = new Contract { Name = string.Empty };
            Assert.Throws<ArgumentException>(() => service.UpdateContract("A", updated));
        }
    }
}
