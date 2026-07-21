using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Philips.IBE.Service.WebAgent.Server.Controllers;
using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Services;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Controllers
{
    public class ServiceNodeControllerTests
    {
        private readonly Mock<INodeService> _serviceNodeServiceMock;
        private readonly Mock<ILogger<ServiceNodeController>> _loggerMock;
        private readonly ServiceNodeController _controller;

        public ServiceNodeControllerTests()
        {
            _serviceNodeServiceMock = new Mock<INodeService>();
            _loggerMock = new Mock<ILogger<ServiceNodeController>>();
            _controller = new ServiceNodeController(_serviceNodeServiceMock.Object, _loggerMock.Object);
        }

        [Fact]
        public void GetAllServiceNodes_ReturnsOk_WithData()
        {
            var expectedData = new ServiceNode();
            _serviceNodeServiceMock.Setup(s => s.GetServiceNode()).Returns(expectedData);

            var result = _controller.GetAllServiceNodes();

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(expectedData, okResult.Value);
        }

        [Fact]
        public void GetAllServiceNodes_ReturnsServerError_OnException()
        {
            _serviceNodeServiceMock.Setup(s => s.GetServiceNode()).Throws(new Exception("fail"));

            var result = _controller.GetAllServiceNodes();

            var objectResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
            Assert.Equal("Internal server error.", objectResult.Value);
        }

        [Fact]
        public void UpdateHTTPServiceNode_ReturnsOk_OnSuccess()
        {
            var config = new ServiceNodeConfiguration();
            _serviceNodeServiceMock.Setup(s => s.UpdateHTTPServiceNode(config)).Returns(true);

            var result = _controller.UpdateHTTPServiceNode(config);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.True((bool)okResult.Value);
        }

        [Fact]
        public void UpdateHTTPServiceNode_ReturnsBadRequest_OnFailure()
        {
            var config = new ServiceNodeConfiguration();
            _serviceNodeServiceMock.Setup(s => s.UpdateHTTPServiceNode(config)).Returns(false);

            var result = _controller.UpdateHTTPServiceNode(config);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.False((bool)badRequest.Value);
        }

        [Fact]
        public void UpdateHTTPServiceNode_ReturnsServerError_OnException()
        {
            var config = new ServiceNodeConfiguration();
            _serviceNodeServiceMock.Setup(s => s.UpdateHTTPServiceNode(config)).Throws(new Exception("fail"));

            var result = _controller.UpdateHTTPServiceNode(config);

            var objectResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
            Assert.Equal("Internal server error.", objectResult.Value);
        }

        [Fact]
        public void UpdateTCPServiceNode_ReturnsOk_OnSuccess()
        {
            var config = new ServiceNodeConfiguration();
            _serviceNodeServiceMock.Setup(s => s.UpdateTCPServiceNode(config)).Returns(true);

            var result = _controller.UpdateTCPServiceNode(config);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.True((bool)okResult.Value);
        }

        [Fact]
        public void UpdateTCPServiceNode_ReturnsBadRequest_OnFailure()
        {
            var config = new ServiceNodeConfiguration();
            _serviceNodeServiceMock.Setup(s => s.UpdateTCPServiceNode(config)).Returns(false);

            var result = _controller.UpdateTCPServiceNode(config);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.False((bool)badRequest.Value);
        }

        [Fact]
        public void UpdateTCPServiceNode_ReturnsServerError_OnException()
        {
            var config = new ServiceNodeConfiguration();
            _serviceNodeServiceMock.Setup(s => s.UpdateTCPServiceNode(config)).Throws(new Exception("fail"));

            var result = _controller.UpdateTCPServiceNode(config);

            var objectResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
            Assert.Equal("Internal server error.", objectResult.Value);
        }

        [Fact]
        public void UpdateWebSocketServiceNode_ReturnsOk_OnSuccess()
        {
            var config = new ServiceNodeConfiguration();
            _serviceNodeServiceMock.Setup(s => s.UpdateWebSocketClientServiceNode(config)).Returns(true);

            var result = _controller.UpdateWebSocketServiceNode(config);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.True((bool)okResult.Value);
        }

        [Fact]
        public void UpdateWebSocketServiceNode_ReturnsBadRequest_OnFailure()
        {
            var config = new ServiceNodeConfiguration();
            _serviceNodeServiceMock.Setup(s => s.UpdateWebSocketClientServiceNode(config)).Returns(false);

            var result = _controller.UpdateWebSocketServiceNode(config);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.False((bool)badRequest.Value);
        }

        [Fact]
        public void UpdateWebSocketServiceNode_ReturnsServerError_OnException()
        {
            var config = new ServiceNodeConfiguration();
            _serviceNodeServiceMock.Setup(s => s.UpdateWebSocketClientServiceNode(config)).Throws(new Exception("fail"));

            var result = _controller.UpdateWebSocketServiceNode(config);

            var objectResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
            Assert.Equal("Internal server error.", objectResult.Value);
        }

        [Fact]
        public void UpdateADTServiceNode_ReturnsOk_OnSuccess()
        {
            var config = new ServiceNodeConfiguration();
            _serviceNodeServiceMock.Setup(s => s.UpdateADTServiceNode(config)).Returns(true);

            var result = _controller.UpdateADTServiceNode(config);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.True((bool)okResult.Value);
        }

        [Fact]
        public void UpdateADTServiceNode_ReturnsBadRequest_OnFailure()
        {
            var config = new ServiceNodeConfiguration();
            _serviceNodeServiceMock.Setup(s => s.UpdateADTServiceNode(config)).Returns(false);

            var result = _controller.UpdateADTServiceNode(config);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.False((bool)badRequest.Value);
        }

        [Fact]
        public void UpdateADTServiceNode_ReturnsServerError_OnException()
        {
            var config = new ServiceNodeConfiguration();
            _serviceNodeServiceMock.Setup(s => s.UpdateADTServiceNode(config)).Throws(new Exception("fail"));

            var result = _controller.UpdateADTServiceNode(config);

            var objectResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status500InternalServerError, objectResult.StatusCode);
            Assert.Equal("Internal server error.", objectResult.Value);
        }
    }
}
