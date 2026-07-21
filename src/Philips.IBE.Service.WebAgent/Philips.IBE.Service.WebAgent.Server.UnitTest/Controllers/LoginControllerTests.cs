// C#
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Philips.IBE.Service.WebAgent.Server.Authentication;
using Philips.IBE.Service.WebAgent.Server.Constants;
using Philips.IBE.Service.WebAgent.Server.Controllers;
using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Services;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Controllers
{
    public class LoginControllerTests
    {
        private readonly Mock<IAuthenticationService> _authServiceMock;
        private readonly Mock<JWTInvalidator> _jwtInvalidatorMock;
        private readonly Mock<ILogger<LoginController>> _loggerMock;
        private readonly LoginController _controller;

        public LoginControllerTests()
        {
            _authServiceMock = new Mock<IAuthenticationService>();
            _jwtInvalidatorMock = new Mock<JWTInvalidator>();
            _loggerMock = new Mock<ILogger<LoginController>>();
            _controller = new LoginController(_authServiceMock.Object, _jwtInvalidatorMock.Object, _loggerMock.Object);
        }

        [Fact]
        public void Login_ReturnsBadRequest_WhenCredentialsFormatIsInvalid()
        {
            // Arrange
            var context = new DefaultHttpContext();
            context.Request.Headers["Authorization"] = "Basic invalidbase64";
            _controller.ControllerContext = new ControllerContext { HttpContext = context };

            // Act
            var result = _controller.Login();

            // Assert
            var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
            var response = Assert.IsType<ResponseModel>(badRequest.Value);
            Assert.Equal(Status.Failure, response.Status);
        }

        [Fact]
        public void Login_ReturnsUnauthorized_WhenLoginFails()
        {
            // Arrange
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:wrongpass"));
            var context = new DefaultHttpContext();
            context.Request.Headers["Authorization"] = $"Basic {credentials}";
            _controller.ControllerContext = new ControllerContext { HttpContext = context };

            _authServiceMock.Setup(s => s.LoginUser("user", "wrongpass"))
                .Returns(new ResponseModel { Status = Status.Failure });

            // Act
            var result = _controller.Login();

            // Assert
            var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
            var response = Assert.IsType<ResponseModel>(unauthorized.Value);
            Assert.Equal(Status.Failure, response.Status);
        }

        [Fact]
        public void Login_ReturnsOk_WhenLoginSucceeds()
        {
            // Arrange
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass"));
            var context = new DefaultHttpContext();
            context.Request.Headers["Authorization"] = $"Basic {credentials}";
            _controller.ControllerContext = new ControllerContext { HttpContext = context };

            var expectedResponse = new ResponseModel { Status = Status.Successful };
            _authServiceMock.Setup(s => s.LoginUser("user", "pass"))
                .Returns(expectedResponse);

            // Act
            var result = _controller.Login();

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal(expectedResponse, okResult.Value);
        }

        [Fact]
        public void Logout_InvalidatesToken_AndReturnsOk()
        {
            var handler = new JwtSecurityTokenHandler();
            var token = handler.WriteToken(new JwtSecurityToken(
                expires: DateTime.UtcNow.AddMinutes(10)
            ));
            var context = new DefaultHttpContext();
            context.Request.Headers["Authorization"] = $"Bearer {token}";
            _controller.ControllerContext = new ControllerContext { HttpContext = context };

            var result = _controller.Logout();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
            var messageProp = okResult.Value.GetType().GetProperty("message");
            Assert.NotNull(messageProp);
            Assert.Equal("Logged out successfully", messageProp.GetValue(okResult.Value));
            _jwtInvalidatorMock.Verify(j => j.AddToken(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Once);
        }

        [Fact]
        public void Logout_WithoutToken_ReturnsOk()
        {

            var context = new DefaultHttpContext();
            _controller.ControllerContext = new ControllerContext { HttpContext = context };

            var result = _controller.Logout();

            var okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);
            var messageProp = okResult.Value.GetType().GetProperty("message");
            Assert.NotNull(messageProp);
            Assert.Equal("Logged out successfully", messageProp.GetValue(okResult.Value));
            _jwtInvalidatorMock.Verify(j => j.AddToken(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
        }

        [Fact]
        public void GetTokenExpiry_Returns_ValidTo_From_Jwt()
        {
            // Arrange
            var handler = new JwtSecurityTokenHandler();
            var expiry = DateTime.UtcNow.AddMinutes(30);
            var token = handler.WriteToken(new JwtSecurityToken(expires: expiry));

            var method = typeof(LoginController).GetMethod("GetTokenExpiry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            var result = (DateTime)method.Invoke(_controller, new object[] { token });

            // Assert
            Assert.Equal(expiry, result, TimeSpan.FromSeconds(1)); 
        }

        [Fact]
        public void GetDecodedBasicAuthHeader_ReturnsDecodedArray_WhenHeaderIsValid()
        {
            // Arrange
            var username = "user";
            var password = "pass";
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            var context = new DefaultHttpContext();
            context.Request.Headers["Authorization"] = $"Basic {credentials}";
            _controller.ControllerContext = new ControllerContext { HttpContext = context };

            var method = typeof(LoginController).GetMethod("GetDecodedBasicAuthHeader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            var result = (string[])method.Invoke(_controller, null);

            // Assert
            Assert.Equal(new[] { username, password }, result);
        }

        [Fact]
        public void GetDecodedBasicAuthHeader_ReturnsEmptyArray_WhenHeaderIsInvalid()
        {
            // Arrange
            var context = new DefaultHttpContext();
            context.Request.Headers["Authorization"] = "Basic notbase64";
            _controller.ControllerContext = new ControllerContext { HttpContext = context };

            var method = typeof(LoginController).GetMethod("GetDecodedBasicAuthHeader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            var result = (string[])method.Invoke(_controller, null);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void GetBasicAuthHeader_ReturnsBase64String_WhenHeaderIsValid()
        {
            // Arrange
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes("user:pass"));
            var context = new DefaultHttpContext();
            context.Request.Headers["Authorization"] = $"Basic {credentials}";
            _controller.ControllerContext = new ControllerContext { HttpContext = context };

            var method = typeof(LoginController).GetMethod("GetBasicAuthHeader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act
            var result = (string)method.Invoke(_controller, null);

            // Assert
            Assert.Equal(credentials, result);
        }

        [Fact]
        public void GetBasicAuthHeader_ThrowsException_WhenHeaderIsMissing()
        {
            // Arrange
            var context = new DefaultHttpContext();
            _controller.ControllerContext = new ControllerContext { HttpContext = context };

            var method = typeof(LoginController).GetMethod("GetBasicAuthHeader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // Act & Assert
            Assert.ThrowsAny<Exception>(() => method.Invoke(_controller, null));
        }


    }
}
