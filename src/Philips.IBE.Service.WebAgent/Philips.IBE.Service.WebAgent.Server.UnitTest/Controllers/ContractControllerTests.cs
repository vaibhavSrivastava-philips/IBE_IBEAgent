// C#
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Philips.IBE.Service.WebAgent.Server.Controllers;
using Philips.IBE.Service.WebAgent.Server.Exceptions;
using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Services;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Controllers
{
    public class ContractControllerTests
    {
        private readonly Mock<IContractService> _serviceMock;
        private readonly Mock<ILogger<ContractController>> _loggerMock;
        private readonly ContractController _controller;

        public ContractControllerTests()
        {
            _serviceMock = new Mock<IContractService>();
            _loggerMock = new Mock<ILogger<ContractController>>();
            _controller = new ContractController(_serviceMock.Object, _loggerMock.Object);
        }

        [Fact]
        public void GetAllContracts_ReturnsOk_WithData()
        {
            var contracts = new List<Contract> { new Contract() };
            _serviceMock.Setup(s => s.GetAllContracts()).Returns(contracts);

            var result = _controller.GetAllContracts();

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(contracts, okResult.Value);
        }

        [Fact]
        public void GetAllContracts_ReturnsServerError_OnException()
        {
            _serviceMock.Setup(s => s.GetAllContracts()).Throws(new Exception("fail"));

            var result = _controller.GetAllContracts();

            var statusResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(500, statusResult.StatusCode);
            Assert.Contains("Internal server error", statusResult.Value.ToString());
        }

        [Fact]
        public void AddContract_ReturnsBadRequest_WhenNull()
        {
            var result = _controller.AddContract(null);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid contract data.", badRequest.Value);
        }

        [Fact]
        public void AddContract_ReturnsOk_WhenSuccess()
        {
            var contract = new Contract();
            _serviceMock.Setup(s => s.AddContract(contract));

            var result = _controller.AddContract(contract);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Contract has been added successfully.", okResult.Value);
        }

        [Fact]
        public void AddContract_ReturnsServerError_OnException()
        {
            var contract = new Contract();
            _serviceMock.Setup(s => s.AddContract(contract)).Throws(new Exception("fail"));

            var result = _controller.AddContract(contract);

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
            Assert.Contains("Internal server error", statusResult.Value.ToString());
        }

        [Fact]
        public void UpdateContract_ReturnsBadRequest_WhenNull()
        {
            var result = _controller.UpdateContract("test", null);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid contract data.", badRequest.Value);
        }

        [Fact]
        public void UpdateContract_ReturnsOk_WhenSuccess()
        {
            var contract = new Contract();
            _serviceMock.Setup(s => s.UpdateContract("test", contract));

            var result = _controller.UpdateContract("test", contract);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Contract has been updated successfully.", okResult.Value);
        }

        [Fact]
        public void UpdateContract_ReturnsNotFound_OnDataNotFoundException()
        {
            var contract = new Contract();
            _serviceMock.Setup(s => s.UpdateContract("test", contract)).Throws(new DataNotFoundException("not found"));

            var result = _controller.UpdateContract("test", contract);

            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("not found", notFound.Value);
        }

        [Fact]
        public void UpdateContract_ReturnsServerError_OnException()
        {
            var contract = new Contract();
            _serviceMock.Setup(s => s.UpdateContract("test", contract)).Throws(new Exception("fail"));

            var result = _controller.UpdateContract("test", contract);

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
            Assert.Contains("Internal server error", statusResult.Value.ToString());
        }

        [Fact]
        public void DeleteContract_ReturnsOk_WhenSuccess()
        {
            _serviceMock.Setup(s => s.DeleteContract("test"));

            var result = _controller.DeleteContract("test");

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Contract has been deleted successfully.", okResult.Value);
        }

        [Fact]
        public void DeleteContract_ReturnsNotFound_OnDataNotFoundException()
        {
            _serviceMock.Setup(s => s.DeleteContract("test")).Throws(new DataNotFoundException("not found"));

            var result = _controller.DeleteContract("test");

            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("not found", notFound.Value);
        }

        [Fact]
        public void DeleteContract_ReturnsServerError_OnException()
        {
            _serviceMock.Setup(s => s.DeleteContract("test")).Throws(new Exception("fail"));

            var result = _controller.DeleteContract("test");

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
            Assert.Contains("Internal server error", statusResult.Value.ToString());
        }
    }
}
