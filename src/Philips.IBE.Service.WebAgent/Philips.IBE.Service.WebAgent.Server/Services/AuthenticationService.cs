using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Philips.IBE.Service.WebAgent.Server.Authentication;
using Philips.IBE.Service.WebAgent.Server.Configuration;
using System.Security.Principal;
using Philips.IBE.Service.WebAgent.Server.Models;
using Philips.IBE.Service.WebAgent.Server.Constants;
using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;

namespace Philips.IBE.Service.WebAgent.Server.Services
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly AppConfiguration _configuration;
        private readonly JwtCreator _jwtCreator;
        private readonly ILogger<AuthenticationService> _logger;

        public AuthenticationService(AppConfiguration configuration, JwtCreator jwtCreator, ILogger<AuthenticationService> logger)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _jwtCreator = jwtCreator ?? throw new ArgumentNullException(nameof(jwtCreator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private static string SanitizeForLogging(string input)
        {
            if (input == null) return null;
            return input.Replace("\r", "").Replace("\n", "");
        }


        [SupportedOSPlatform("windows")]
        public ResponseModel LoginUser(string userName, string password)
        {
            if (string.IsNullOrEmpty(userName))
            {
                throw new ArgumentException("Username cannot be null or empty", nameof(userName));
            }

            var response = new ResponseModel();
            try
            {
                var sanitizedUserName = SanitizeForLogging(userName);
                _logger.LogInformation("Login request received for user: {UserName}", sanitizedUserName);
                var userDetails = GetUserDetails(userName);
                var domain = userDetails[0];
                var user = userDetails[1];

                _logger.LogDebug("Validating AD user: {User}", user);
                if (ValidateADUser(user, password, domain))
                {
                    _logger.LogInformation("User {User} is authenticated", user);
                    var isAdmin = IsInGroup(user, GetAdminGroup());
                    var isMember = IsInGroup(user, GetNormalUserGroup());

                    if (isAdmin || isMember)
                    {
                        _logger.LogInformation("User {User} is part of the AD Group", user);
                        response.Status = Status.Successful;
                        response.Value = _jwtCreator.CreateAccessToken(user, new[] { isAdmin ? Permissions.admin : Permissions.normal });
                        response.DisplayMessage = isAdmin ? "Administrator" : "Normal";
                    }
                    else
                    {
                        _logger.LogInformation("User {User} is not part of the AD Group", user);
                        response.Status = Status.Failure;
                        response.DisplayMessage = "User is authenticated but not a part of the AD Group";
                    }
                }
                else
                {
                    _logger.LogWarning("Invalid credentials for user: {User}", user);
                    response.Status = Status.Failure;
                    response.DisplayMessage = "Invalid Credentials";
                }
            }
            catch (Exception ex)
            {
                var sanitizedUserName = SanitizeForLogging(userName);
                _logger.LogError(ex, "Error occurred during login for user: {UserName}", sanitizedUserName);
                response.Status = Status.Failure;
                response.DisplayMessage = "Error Occurred: " + ex.Message;
            }
            return response;
        }
        [SupportedOSPlatform("windows")]
        protected virtual bool ValidateADUser(string userName, string password, string domain)
        {
            _logger.LogInformation("Validating user credentials for user: {User}", userName);
            using (var pc = new PrincipalContext(ContextType.Domain, domain))
            {
                var isValid = pc.ValidateCredentials(userName, password);
                _logger.LogDebug("Validation result for user {User} is {IsValid}", userName, isValid);
                return isValid;
            }
        }
        [SupportedOSPlatform("windows")]
        protected virtual bool IsInGroup(string user, string group)
        {
            if (string.IsNullOrEmpty(group))
            {
                _logger.LogWarning("Group name is null or empty. Returning false for group membership check.");
                return false;
            }

            _logger.LogInformation("Checking if user {User} is part of the group {Group}", user, group);
            using (var identity = new WindowsIdentity(user))
            {
                var principal = new WindowsPrincipal(identity);
                var isInRole = principal.IsInRole(group);
                _logger.LogDebug("User {User} is in group {Group}: {IsInRole}", user, group, isInRole);
                return isInRole;
            }
        }

        protected virtual string[] GetUserDetails(string userName)
        {
            var splitArray = userName.Split("\\");
            if (splitArray.Length != 2)
            {
                _logger.LogError("Invalid user name or domain for user: {UserName}", userName);
                throw new Exception("Invalid user name or domain");
            }
            _logger.LogInformation("User name and domain split for user: {UserName}", userName);
            return splitArray;
        }

        private string GetAdminGroup()
        {

            return _configuration.AuthenticationConfiguration.AdminUserGroup;
        }

        private string GetNormalUserGroup()
        {
            return _configuration.AuthenticationConfiguration.NormalUserGroup;
        }
    }
}