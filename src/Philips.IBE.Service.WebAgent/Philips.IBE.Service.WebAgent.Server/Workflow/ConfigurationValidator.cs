using System;

namespace Philips.IBE.Service.WebAgent.Server.Configuration
{
    public class ConfigurationValidator
    {
        private readonly IConfiguration _configuration;

        public ConfigurationValidator(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public JwtOptions GetJwtOptions()
        {
            var jwtOptions = _configuration.GetSection("JwtOptions").Get<JwtOptions>();
            if (jwtOptions == null)
                throw new InvalidOperationException("JwtOptions section is missing or invalid in configuration.");
            return jwtOptions;
        }

        public void ValidateJwtOptions(JwtOptions jwtOptions)
        {
            if (string.IsNullOrWhiteSpace(jwtOptions.Issuer))
                throw new ArgumentException("Issuer cannot be null or empty in JwtOptions.");

            if (string.IsNullOrWhiteSpace(jwtOptions.Audience))
                throw new ArgumentException("Audience cannot be null or empty in JwtOptions.");

            if (jwtOptions.ExpirationSeconds <= 0)
                throw new ArgumentException("ExpirationSeconds must be greater than zero in JwtOptions.");
        }

        public AuthenticationConfiguration GetAuthenticationConfiguration()
        {
            var authConfig = _configuration.GetSection("AuthenticationConfiguration").Get<AuthenticationConfiguration>();
            if (authConfig == null)
                throw new InvalidOperationException("AuthenticationConfiguration section is missing or invalid in configuration.");
            return authConfig;
        }

        public void ValidateAuthenticationConfiguration(AuthenticationConfiguration authConfig)
        {
            if (string.IsNullOrWhiteSpace(authConfig.AuthenticationMode))
                throw new ArgumentException("AuthenticationMode cannot be null or empty in AuthenticationConfiguration.");

            if (string.IsNullOrWhiteSpace(authConfig.AdminUserGroup))
                throw new ArgumentException("AdminUserGroup cannot be null or empty in AuthenticationConfiguration.");

            if (string.IsNullOrWhiteSpace(authConfig.NormalUserGroup))
                throw new ArgumentException("NormalUserGroup cannot be null or empty in AuthenticationConfiguration.");
        }

        public CommonConfiguration GetCommonConfiguration()
        {
            var commonConfig = _configuration.GetSection("CommonConfiguration").Get<CommonConfiguration>();
            if (commonConfig == null)
                throw new InvalidOperationException("CommonConfiguration section is missing or invalid in configuration.");
            return commonConfig;
        }

        public void ValidateCommonConfiguration(CommonConfiguration commonConfig)
        {
            if (string.IsNullOrWhiteSpace(commonConfig.FolderPath))
                throw new ArgumentException("FolderPath cannot be null or empty in CommonConfiguration.");

            if (string.IsNullOrWhiteSpace(commonConfig.CertificateFolderName))
                throw new ArgumentException("CertificateFolderName cannot be null or empty in CommonConfiguration.");

            if (string.IsNullOrWhiteSpace(commonConfig.ServiceConfigPath))
                throw new ArgumentException("ServiceConfigPath cannot be null or empty in CommonConfiguration.");
            if (string.IsNullOrWhiteSpace(commonConfig.License))
                throw new ArgumentException("License cannot be null or empty in CommonConfiguration.");
        }

    }
}
