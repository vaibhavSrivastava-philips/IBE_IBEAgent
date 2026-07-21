using System;
using Microsoft.Extensions.Configuration;
using Moq;
using Philips.IBE.Service.WebAgent.Server.Configuration;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Workflow;
public class ConfigurationValidatorTests
{
    [Theory]
    [InlineData(null, "aud", 10, "Issuer cannot be null or empty in JwtOptions.")]
    [InlineData("", "aud", 10, "Issuer cannot be null or empty in JwtOptions.")]
    [InlineData("iss", null, 10, "Audience cannot be null or empty in JwtOptions.")]
    [InlineData("iss", "", 10, "Audience cannot be null or empty in JwtOptions.")]
    [InlineData("iss", "aud", 0, "ExpirationSeconds must be greater than zero in JwtOptions.")]
    [InlineData("iss", "aud", -1, "ExpirationSeconds must be greater than zero in JwtOptions.")]
    public void ValidateJwtOptions_Throws_OnInvalid(string issuer, string audience, int exp, string expectedMsg)
    {
        var validator = new ConfigurationValidator(new Mock<IConfiguration>().Object);
        var options = new JwtOptions { Issuer = issuer, Audience = audience, ExpirationSeconds = exp };

        var ex = Assert.Throws<ArgumentException>(() => validator.ValidateJwtOptions(options));
        Assert.Contains(expectedMsg, ex.Message);
    }

    [Fact]
    public void ValidateJwtOptions_DoesNotThrow_OnValid()
    {
        var validator = new ConfigurationValidator(new Mock<IConfiguration>().Object);
        var options = new JwtOptions { Issuer = "iss", Audience = "aud", ExpirationSeconds = 10 };

        validator.ValidateJwtOptions(options);
    }

    [Fact]
    public void GetAuthenticationConfiguration_ReturnsOptions_WhenSectionExists()
    {
        var inMemorySettings = new Dictionary<string, string>
    {
        {"AuthenticationConfiguration:AuthenticationMode", "mode"},
        {"AuthenticationConfiguration:AdminUserGroup", "admin"},
        {"AuthenticationConfiguration:NormalUserGroup", "user"}
    };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var validator = new ConfigurationValidator(configuration);

        var result = validator.GetAuthenticationConfiguration();

        Assert.Equal("mode", result.AuthenticationMode);
        Assert.Equal("admin", result.AdminUserGroup);
        Assert.Equal("user", result.NormalUserGroup);
    }


    [Theory]
    [InlineData(null, "admin", "user", "AuthenticationMode cannot be null or empty in AuthenticationConfiguration.")]
    [InlineData("", "admin", "user", "AuthenticationMode cannot be null or empty in AuthenticationConfiguration.")]
    [InlineData("mode", null, "user", "AdminUserGroup cannot be null or empty in AuthenticationConfiguration.")]
    [InlineData("mode", "", "user", "AdminUserGroup cannot be null or empty in AuthenticationConfiguration.")]
    [InlineData("mode", "admin", null, "NormalUserGroup cannot be null or empty in AuthenticationConfiguration.")]
    [InlineData("mode", "admin", "", "NormalUserGroup cannot be null or empty in AuthenticationConfiguration.")]
    public void ValidateAuthenticationConfiguration_Throws_OnInvalid(string mode, string admin, string user, string expectedMsg)
    {
        var validator = new ConfigurationValidator(new Mock<IConfiguration>().Object);
        var config = new AuthenticationConfiguration { AuthenticationMode = mode, AdminUserGroup = admin, NormalUserGroup = user };

        var ex = Assert.Throws<ArgumentException>(() => validator.ValidateAuthenticationConfiguration(config));
        Assert.Contains(expectedMsg, ex.Message);
    }

    [Fact]
    public void ValidateAuthenticationConfiguration_DoesNotThrow_OnValid()
    {
        var validator = new ConfigurationValidator(new Mock<IConfiguration>().Object);
        var config = new AuthenticationConfiguration { AuthenticationMode = "mode", AdminUserGroup = "admin", NormalUserGroup = "user" };

        validator.ValidateAuthenticationConfiguration(config);
    }

    [Theory]
    [InlineData(null, "certs", "FolderPath cannot be null or empty in CommonConfiguration.")]
    [InlineData("", "certs", "FolderPath cannot be null or empty in CommonConfiguration.")]
    [InlineData("path", null, "CertificateFolderName cannot be null or empty in CommonConfiguration.")]
    [InlineData("path", "", "CertificateFolderName cannot be null or empty in CommonConfiguration.")]
    public void ValidateCommonConfiguration_Throws_OnInvalid(string folder, string cert, string expectedMsg)
    {
        var validator = new ConfigurationValidator(new Mock<IConfiguration>().Object);
        var config = new CommonConfiguration { FolderPath = folder, CertificateFolderName = cert };

        var ex = Assert.Throws<ArgumentException>(() => validator.ValidateCommonConfiguration(config));
        Assert.Contains(expectedMsg, ex.Message);
    }

    [Fact]
    public void ValidateCommonConfiguration_DoesNotThrow_OnValid()
    {
        var validator = new ConfigurationValidator(new Mock<IConfiguration>().Object);
        var config = new CommonConfiguration { FolderPath = "path", CertificateFolderName = "certs" };

        validator.ValidateCommonConfiguration(config);
    }
}
