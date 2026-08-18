// C#
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Philips.IBE.Service.WebAgent.Server.Controllers;
using Philips.IBE.Service.WebAgent.Server.DBUtilities;
using Philips.IBE.Service.WebAgent.Server.Models;
using System.Collections.Generic;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Controllers
{
    public class ErrorQueueControllerTests
    {
        private readonly Mock<IDBUtils> _dbUtilsMock;
        private readonly Mock<ILogger<ErrorQueueController>> _loggerMock;
        private readonly ErrorQueueController _controller;

        public ErrorQueueControllerTests()
        {
            _dbUtilsMock = new Mock<IDBUtils>();
            _loggerMock = new Mock<ILogger<ErrorQueueController>>();
            _controller = new ErrorQueueController(_dbUtilsMock.Object, _loggerMock.Object);
        }

        [Fact]
        public void Get_ReturnsOk_WhenErrorQueueExists()
        {
            var errorQueueList = new List<ErrorQueue> { new ErrorQueue { Message = [] } };
            _dbUtilsMock.Setup(d => d.FetchErrorQueue()).Returns(errorQueueList);

            var result = _controller.Get();

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(errorQueueList, okResult.Value);
        }

        [Fact]
        public void Get_ReturnsServerError_WhenErrorQueueIsNull()
        {
            _dbUtilsMock.Setup(d => d.FetchErrorQueue()).Returns((List<ErrorQueue>)null!);

            var resultNull = _controller.Get();
            var errorResult = Assert.IsType<ObjectResult>(resultNull.Result);
            Assert.Equal(500, errorResult.StatusCode);
            Assert.Equal("Failed to fetch error queue.", errorResult.Value);
        }

        [Fact]
        public void Get_ReturnsOk_WhenErrorQueueIsEmpty()
        {
            _dbUtilsMock.Setup(d => d.FetchErrorQueue()).Returns(new List<ErrorQueue>());

            var resultEmpty = _controller.Get();
            var okResult = Assert.IsType<OkObjectResult>(resultEmpty.Result);
        }


        [Fact]
        public void UpdateStatus_ReturnsBadRequest_WhenIdIsInvalid()
        {
            var result = _controller.UpdateStatus(0);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid ID.", badRequest.Value);
        }

        [Fact]
        public void UpdateStatus_ReturnsOk_WhenUpdateSucceeds()
        {
            _dbUtilsMock.Setup(d => d.UpdateStatus("PROCESS_REQUESTED", "5")).Returns(true);

            var result = _controller.UpdateStatus(5);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Status updated successfully.", okResult.Value);
        }

        [Fact]
        public void UpdateStatus_ReturnsServerError_WhenUpdateFails()
        {
            _dbUtilsMock.Setup(d => d.UpdateStatus("PROCESS_REQUESTED", "5")).Returns(false);

            var result = _controller.UpdateStatus(5);

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
            Assert.Equal("Failed to update status.", statusResult.Value);
        }

        [Fact]
        public void UpdateStatus_Calls_DbUtils_With_Correct_Parameters()
        {
            _dbUtilsMock.Setup(d => d.UpdateStatus(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

            _controller.UpdateStatus(42);

            _dbUtilsMock.Verify(d => d.UpdateStatus("PROCESS_REQUESTED", "42"), Times.Once);
        }


    }
}
