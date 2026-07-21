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
    public class CommunicationPointControllerTests
    {
        private readonly Mock<ICommunicationDataService> _serviceMock;
        private readonly Mock<ILogger<CommunicationPointController>> _loggerMock;
        private readonly CommunicationPointController _controller;

        public CommunicationPointControllerTests()
        {
            _serviceMock = new Mock<ICommunicationDataService>();
            _loggerMock = new Mock<ILogger<CommunicationPointController>>();
            _controller = new CommunicationPointController(_serviceMock.Object, _loggerMock.Object);
        }

        [Fact]
        public void GetAllCommunicationPoints_ReturnsOk_WithData()
        {
            var data = new List<CommunicationPoint> { new CommunicationPoint() };
            _serviceMock.Setup(s => s.GetAllCommunicationData()).Returns(data);

            var result = _controller.GetAllCommunicationPoints();

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(data, okResult.Value);
        }

        [Fact]
        public void GetAllCommunicationPoints_ReturnsServerError_OnException()
        {
            _serviceMock.Setup(s => s.GetAllCommunicationData()).Throws(new Exception("fail"));

            var result = _controller.GetAllCommunicationPoints();

            var statusResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(500, statusResult.StatusCode);
            Assert.Equal("Internal server error.", statusResult.Value);
        }

        [Fact]
        public void GetCommunicationPointById_ReturnsOk_WhenFound()
        {
            var point = new CommunicationPoint { Id = 1 };
            _serviceMock.Setup(s => s.GetCommunicationDataById(1)).Returns(point);

            var result = _controller.GetCommunicationPointById(1);

            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(point, okResult.Value);
        }

        [Fact]
        public void GetCommunicationPointById_ReturnsNotFound_WhenNull()
        {
            _serviceMock.Setup(s => s.GetCommunicationDataById(2)).Returns((CommunicationPoint)null);
            var result = _controller.GetCommunicationPointById(2);

            var notFound = Assert.IsType<NotFoundObjectResult>(result.Result);
            Assert.Equal("Communication point with ID 2 not found.", notFound.Value);
        }

        [Fact]
        public void GetCommunicationPointById_ReturnsServerError_OnException()
        {
            _serviceMock.Setup(s => s.GetCommunicationDataById(3)).Throws(new Exception("fail"));

            var result = _controller.GetCommunicationPointById(3);

            var statusResult = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(500, statusResult.StatusCode);
            Assert.Equal("Internal server error.", statusResult.Value);
        }

        [Fact]
        public void AddCommunicationPoint_ReturnsBadRequest_WhenNull()
        {
            var result = _controller.AddCommunicationPoint(null);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid communication point data.", badRequest.Value);
        }

        [Fact]
        public void AddCommunicationPoint_ReturnsOk_WhenSuccess()
        {
            var point = new CommunicationPoint();
            _serviceMock.Setup(s => s.AddCommunicationData(point));

            var result = _controller.AddCommunicationPoint(point);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Communication point has been added successfully.", okResult.Value);
        }

        [Fact]
        public void AddCommunicationPoint_ReturnsServerError_OnException()
        {
            var point = new CommunicationPoint();
            _serviceMock.Setup(s => s.AddCommunicationData(point)).Throws(new Exception("fail"));

            var result = _controller.AddCommunicationPoint(point);

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
            Assert.Equal("Internal server error.", statusResult.Value);
        }

        [Fact]
        public void UpdateCommunicationPoint_ReturnsBadRequest_WhenNull()
        {
            var result = _controller.UpdateCommunicationPoint(1, null);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Invalid communication point data.", badRequest.Value);
        }

        [Fact]
        public void UpdateCommunicationPoint_ReturnsOk_WhenSuccess()
        {
            var point = new CommunicationPoint();
            _serviceMock.Setup(s => s.UpdateCommunicationData(1, point));

            var result = _controller.UpdateCommunicationPoint(1, point);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Communication point has been updated successfully.", okResult.Value);
        }

        [Fact]
        public void UpdateCommunicationPoint_ReturnsNotFound_OnDataNotFoundException()
        {
            var point = new CommunicationPoint();
            _serviceMock.Setup(s => s.UpdateCommunicationData(2, point)).Throws(new DataNotFoundException("not found"));

            var result = _controller.UpdateCommunicationPoint(2, point);

            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("not found", notFound.Value);
        }

        [Fact]
        public void UpdateCommunicationPoint_ReturnsServerError_OnException()
        {
            var point = new CommunicationPoint();
            _serviceMock.Setup(s => s.UpdateCommunicationData(3, point)).Throws(new Exception("fail"));

            var result = _controller.UpdateCommunicationPoint(3, point);

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
            Assert.Equal("Internal server error.", statusResult.Value);
        }

        [Fact]
        public void DeleteCommunicationPoint_ReturnsOk_WhenSuccess()
        {
            _serviceMock.Setup(s => s.DeleteCommunicationData(1));

            var result = _controller.DeleteCommunicationPoint(1);

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.Equal("Communication point has been deleted successfully.", okResult.Value);
        }

        [Fact]
        public void DeleteCommunicationPoint_ReturnsNotFound_OnDataNotFoundException()
        {
            _serviceMock.Setup(s => s.DeleteCommunicationData(2)).Throws(new DataNotFoundException("not found"));

            var result = _controller.DeleteCommunicationPoint(2);

            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("not found", notFound.Value);
        }

        [Fact]
        public void DeleteCommunicationPoint_ReturnsServerError_OnException()
        {
            _serviceMock.Setup(s => s.DeleteCommunicationData(3)).Throws(new Exception("fail"));

            var result = _controller.DeleteCommunicationPoint(3);

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
            Assert.Equal("Internal server error.", statusResult.Value);
        }
    }
}
