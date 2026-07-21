// C#
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Philips.IBE.Service.WebAgent.Server.Authentication;
using Philips.IBE.Service.WebAgent.Server.Configuration;
using Philips.IBE.Service.WebAgent.Server.Constants;
using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Services;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Services
{
    public class AuthenticationServiceTests
    {
        private readonly AppConfiguration _config;
        private readonly Mock<ILogger<AuthenticationService>> _mockLogger;
        private readonly TestableAuthenticationService _service;

        public AuthenticationServiceTests()
        {
            _config = CreateConfig();
            _mockLogger = new Mock<ILogger<AuthenticationService>>();
            var realJwtCreator = new JwtCreator(_config);
            _service = new TestableAuthenticationService(_config, realJwtCreator, _mockLogger.Object);
        }

        private AppConfiguration CreateConfig()
        {
            var inMemorySettings = new Dictionary<string, string?>
            {
                { "AuthenticationConfiguration:AdminUserGroup", "AdminGroup" },
                { "AuthenticationConfiguration:NormalUserGroup", "UserGroup" },
                { "AuthenticationConfiguration:AuthenticationMode", "ActiveDirectory" },
                { "JwtOptions:Issuer", "TestIssuer" },
                { "JwtOptions:Audience", "TestAudience" },
                { "JwtOptions:ExpirationSeconds", "3600" },
                { "CommonConfiguration:FolderPath", "C:\\Temp" },
                { "CommonConfiguration:DatabaseEnabled", "true" },
                { "CommonConfiguration:CertificateFolderName", "certs" },
                { "CommonConfiguration:DatabaseFileName", "testdb" }
            };
            var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();
            return new AppConfiguration(configuration);
        }

        private class TestableAuthenticationService : AuthenticationService
        {
            public bool ValidateADUserResult { get; set; }
            public bool IsAdmin { get; set; }
            public bool IsMember { get; set; }
            public string[] UserDetails { get; set; } = new[] { "DOMAIN", "user" };
            public Exception? GetUserDetailsException { get; set; }

            public TestableAuthenticationService(AppConfiguration config, JwtCreator jwt, ILogger<AuthenticationService> logger)
                : base(config, jwt, logger) { }

            protected override bool ValidateADUser(string userName, string password, string domain) => ValidateADUserResult;
            protected override bool IsInGroup(string user, string group) => group == "AdminGroup" ? IsAdmin : IsMember;
            protected override string[] GetUserDetails(string userName)
            {
                if (GetUserDetailsException != null)
                    throw GetUserDetailsException;
                return UserDetails;
            }
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public void LoginUser_ReturnsToken_WhenCredentialsAreValid()
        {
            var config = CreateConfig();
            var jwtCreator = new JwtCreator(config);
            var logger = new Mock<ILogger<AuthenticationService>>().Object;

            // Use the testable service to control authentication behavior
            var service = new TestableAuthenticationService(config, jwtCreator, logger)
            {
                ValidateADUserResult = true,
                IsAdmin = true,
                IsMember = false
            };

            var result = service.LoginUser("DOMAIN\\user", "password");

            Assert.Equal(Status.Successful, result.Status);
            Assert.False(string.IsNullOrEmpty(result.Value as string));
        }


        [Fact]
        [SupportedOSPlatform("windows")]
        public void LoginUser_ThrowsArgumentException_WhenUserNameIsNullOrEmpty()
        {
            Assert.Throws<ArgumentException>(() => _service.LoginUser(null!, "password"));
            Assert.Throws<ArgumentException>(() => _service.LoginUser(string.Empty, "password"));
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public void LoginUser_ReturnsFailure_WhenUserNameFormatIsInvalid()
        {
            _service.GetUserDetailsException = new Exception("Invalid user name or domain");
            var result = _service.LoginUser("invalidUserName", "password");
            Assert.Equal(Status.Failure, result.Status);
            Assert.Contains("Error Occurred", result.DisplayMessage);
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public void LoginUser_ReturnsFailure_WhenCredentialsInvalid()
        {
            _service.ValidateADUserResult = false;
            var result = _service.LoginUser("DOMAIN\\user", "password");
            Assert.Equal(Status.Failure, result.Status);
            Assert.Equal("Invalid Credentials", result.DisplayMessage);
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public void LoginUser_ReturnsFailure_WhenUserNotInAnyGroup()
        {
            _service.ValidateADUserResult = true;
            _service.IsAdmin = false;
            _service.IsMember = false;
            var result = _service.LoginUser("DOMAIN\\user", "password");
            Assert.Equal(Status.Failure, result.Status);
            Assert.Contains("not a part of the AD Group", result.DisplayMessage);
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public void LoginUser_ReturnsSuccess_WhenUserIsAdmin()
        {
            _service.ValidateADUserResult = true;
            _service.IsAdmin = true;
            _service.IsMember = false;

            var result = _service.LoginUser("DOMAIN\\user", "password");

            Assert.Equal(Status.Successful, result.Status);
            Assert.False(string.IsNullOrEmpty(result.Value as string));
            Assert.Equal("Administrator", result.DisplayMessage);
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public void LoginUser_ReturnsSuccess_WhenUserIsMember()
        {
            _service.ValidateADUserResult = true;
            _service.IsAdmin = false;
            _service.IsMember = true;

            var result = _service.LoginUser("DOMAIN\\user", "password");

            Assert.Equal(Status.Successful, result.Status);
            Assert.False(string.IsNullOrEmpty(result.Value as string));
            Assert.Equal("Normal", result.DisplayMessage);
        }

        [Fact]
        [SupportedOSPlatform("windows")]
        public void LoginUser_LogsInformation_WhenLoginAttempted()
        {
            _service.ValidateADUserResult = false;
            _service.LoginUser("DOMAIN\\user", "password");

            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Login request received") == true),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
}
