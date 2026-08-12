using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Philips.IBE.Service.WebAgent.Server.Authentication;
using Philips.IBE.Service.WebAgent.Server.Configuration;
using Philips.IBE.Service.WebAgent.Server.Models;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Authentication
{

    public class JwtCreatorTests
    {
        private AppConfiguration GetValidAppConfig(
            string signingKey = "supersecretkey1234567890",
            string issuer = "TestIssuer",
            string audience = "TestAudience",
            int expirationSeconds = 3600)
        {
            var inMemorySettings = new Dictionary<string, string?>
{
    {"AuthenticationConfiguration:AuthenticationMode", "TestMode"},
    {"AuthenticationConfiguration:AdminUserGroup", "AdminGroup"},
    {"AuthenticationConfiguration:NormalUserGroup", "UserGroup"},
    {"JwtOptions:Issuer", issuer},
    {"JwtOptions:Audience", audience},
    {"JwtOptions:ExpirationSeconds", expirationSeconds.ToString()},
    {"JwtOptions:SigningKey", signingKey},
    {"CommonConfiguration:DatabaseEnabled", "false"},
    {"CommonConfiguration:CertificateFolderName", "certs"},
    {"CommonConfiguration:DatabaseFileName", "testdb.json"},
    {"CommonConfiguration:FolderPath", "testfolder"},
    {"CommonConfiguration:ServiceConfigPath", "servicesettings.json"}
};

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            return new AppConfiguration(configuration);
        }

        [Fact]
        public void Constructor_Throws_When_JwtOptions_Is_Missing()
        {
            var configuration = new ConfigurationBuilder().Build();
            Assert.Throws<Philips.IBE.Service.WebAgent.Server.Exceptions.DataNotFoundException>(
                () => new JwtCreator(new AppConfiguration(configuration)));
        }


        [Fact]
        public void CreateAccessToken_Expiration_Is_Set()
        {
            var config = GetValidAppConfig(expirationSeconds: 60);
            var creator = new JwtCreator(config);

            var token = creator.CreateAccessToken("user", Array.Empty<Permissions>());
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            Assert.True((jwt.ValidTo - DateTime.UtcNow).TotalSeconds <= 60);
        }
    }
}
