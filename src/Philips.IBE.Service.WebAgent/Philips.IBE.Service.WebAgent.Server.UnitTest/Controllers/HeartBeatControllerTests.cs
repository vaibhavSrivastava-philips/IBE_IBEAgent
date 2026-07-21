using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Philips.IBE.Service.WebAgent.Server.Controllers;
using Philips.IBE.Service.WebAgent.Server.Services;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Controllers
{
    public class HeartBeatControllerTests
    {
        private readonly Mock<IHeartBeatService> _serviceMock;
        private readonly Mock<ILogger<HeartBeatController>> _loggerMock;
        private readonly HeartBeatController _controller;

        public HeartBeatControllerTests()
        {
            _serviceMock = new Mock<IHeartBeatService>();
            _loggerMock = new Mock<ILogger<HeartBeatController>>();
            _controller = new HeartBeatController(_serviceMock.Object, _loggerMock.Object);
        }

        [Fact]
        public async Task GetServerStatus_ReturnsOk_WhenPortIsOpen()
        {
            _serviceMock.Setup(s => s.IsPortOpenAsync("localhost", 80)).ReturnsAsync(true);

            var result = await _controller.GetServerStatus("localhost", 80);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);

            var valueType = okResult.Value.GetType();
            Assert.Equal("localhost", valueType.GetProperty("Host")?.GetValue(okResult.Value));
            Assert.Equal(80, valueType.GetProperty("Port")?.GetValue(okResult.Value));
            Assert.True((bool)valueType.GetProperty("IsOpen")?.GetValue(okResult.Value));
            Assert.Equal("open", valueType.GetProperty("Status")?.GetValue(okResult.Value));
        }

        [Fact]
        public async Task GetServerStatus_ReturnsOk_WhenPortIsClosed()
        {
            _serviceMock.Setup(s => s.IsPortOpenAsync("localhost", 81)).ReturnsAsync(false);

            var result = await _controller.GetServerStatus("localhost", 81);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);

            var valueType = okResult.Value.GetType();
            Assert.Equal("localhost", valueType.GetProperty("Host")?.GetValue(okResult.Value));
            Assert.Equal(81, valueType.GetProperty("Port")?.GetValue(okResult.Value));
            Assert.False((bool)valueType.GetProperty("IsOpen")?.GetValue(okResult.Value));
            Assert.Equal("closed", valueType.GetProperty("Status")?.GetValue(okResult.Value));
        }

        [Theory]
        [InlineData(null, 80)]
        [InlineData("", 80)]
        [InlineData("localhost", 0)]
        [InlineData("localhost", -1)]
        [InlineData("localhost", 70000)]
        public async Task GetServerStatus_ReturnsBadRequest_OnInvalidInput(string host, int port)
        {
            var result = await _controller.GetServerStatus(host, port);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid host or port.", badRequest.Value);
        }

        [Fact]
        public async Task GetServerStatus_ReturnsServerError_OnException()
        {
            _serviceMock.Setup(s => s.IsPortOpenAsync("localhost", 80)).ThrowsAsync(new Exception("fail"));

            var result = await _controller.GetServerStatus("localhost", 80);

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
            Assert.Equal("Internal server error.", statusResult.Value);
        }

        [Fact]
        public void GetClientTcpPorts_ReturnsOk_WhenSuccess()
        {
            var tcpLines = new List<string> { "TCP 127.0.0.1:1234" };
            _serviceMock.Setup(s => s.GetTcpPorts()).Returns(tcpLines);

            var result = _controller.GetClientTcpPorts();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);

            var valueType = okResult.Value.GetType();
            var tcpPortsProp = valueType.GetProperty("TcpPorts");
            Assert.NotNull(tcpPortsProp);
            var value = tcpPortsProp.GetValue(okResult.Value) as List<string>;
            Assert.NotNull(value);
            Assert.Equal(tcpLines, value);
        }

        [Fact]
        public void GetClientTcpPorts_ReturnsServerError_OnException()
        {
            _serviceMock.Setup(s => s.GetTcpPorts()).Throws(new Exception("fail"));

            var result = _controller.GetClientTcpPorts();

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);

            Assert.NotNull(statusResult.Value);
            var valueType = statusResult.Value.GetType();
            var messageProp = valueType.GetProperty("message");
            var errorProp = valueType.GetProperty("error");
            Assert.NotNull(messageProp);
            Assert.NotNull(errorProp);
            Assert.Equal("An error occurred while running netstat", messageProp.GetValue(statusResult.Value));
            Assert.Equal("fail", errorProp.GetValue(statusResult.Value));
        }
    }
}
