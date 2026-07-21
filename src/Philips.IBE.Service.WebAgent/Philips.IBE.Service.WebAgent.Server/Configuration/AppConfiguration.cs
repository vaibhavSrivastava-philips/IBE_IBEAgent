using Microsoft.Extensions.Configuration;
using Philips.IBE.Service.WebAgent.Server.Exceptions;
using System.Configuration;

namespace Philips.IBE.Service.WebAgent.Server.Configuration
{
    public class AppConfiguration
    {

        public AuthenticationConfiguration AuthenticationConfiguration { get; private set; } 
        public JwtOptions JwtOptions { get; private set; } 
        public CommonConfiguration? CommonConfiguration { get; private set; }

        public AppConfiguration(IConfiguration configuration) {

            LoadConfiguration(configuration);
            if (AuthenticationConfiguration == null || JwtOptions == null)
            {
                throw new DataNotFoundException("AuthenticationConfiguration and JwtOptions are mandatory and must be provided.");
            }
        }

        public void LoadConfiguration(IConfiguration configuration)
        {
            AuthenticationConfiguration = new AuthenticationConfiguration
            {
                AuthenticationMode = FetchConfiguration(configuration, "AuthenticationConfiguration:AuthenticationMode"),
                AdminUserGroup = FetchConfiguration(configuration, "AuthenticationConfiguration:AdminUserGroup"),
                NormalUserGroup = FetchConfiguration(configuration, "AuthenticationConfiguration:NormalUserGroup")
            };
            JwtOptions = new JwtOptions
            {
                Issuer = FetchConfiguration(configuration, "JwtOptions:Issuer"),
                Audience = FetchConfiguration(configuration, "JwtOptions:Audience"),
                ExpirationSeconds = int.Parse(FetchConfiguration(configuration, "JwtOptions:ExpirationSeconds"))
            };
            CommonConfiguration = new CommonConfiguration
            {
                FolderPath = FetchConfiguration(configuration, "CommonConfiguration:FolderPath"),
                DatabaseEnabled = bool.Parse(FetchConfiguration(configuration, "CommonConfiguration:DatabaseEnabled")),
                CertificateFolderName = FetchConfiguration(configuration, "CommonConfiguration:CertificateFolderName"),
                DatabaseFileName = FetchConfiguration(configuration, "CommonConfiguration:DatabaseFileName"),
                ServiceConfigPath = FetchConfiguration(configuration, "CommonConfiguration:ServiceConfigPath")
            };
        }

        private string FetchConfiguration(IConfiguration configuration, string key)
        {
            var value = configuration[key];
            if (string.IsNullOrEmpty(value))
            {
                throw new DataNotFoundException($"Configuration key {key} not found or is empty");
            }
            return value;
        }
    }
}
