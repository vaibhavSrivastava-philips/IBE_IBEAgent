using Microsoft.IdentityModel.Tokens;
using Philips.IBE.Service.WebAgent.Server.Configuration;
using Philips.IBE.Service.WebAgent.Server.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Philips.IBE.Service.WebAgent.Server.Authentication
{
    public class JwtCreator
    {
        private readonly JwtOptions _jwtOptions;
        private readonly SymmetricSecurityKey _symmetricKey;
        private readonly SigningCredentials _signingCredentials;

        public JwtCreator(AppConfiguration appConfiguration)
        {
            _jwtOptions = appConfiguration.JwtOptions ?? throw new ArgumentNullException(nameof(appConfiguration.JwtOptions));

            if (string.IsNullOrEmpty(_jwtOptions.SigningKey))
            {
                throw new InvalidOperationException("JWT signing key is not set.");
            }

            var keyBytes = Encoding.UTF8.GetBytes(_jwtOptions.SigningKey);
            _symmetricKey = new SymmetricSecurityKey(keyBytes);
            _signingCredentials = new SigningCredentials(_symmetricKey, SecurityAlgorithms.HmacSha256);
        }

        public string CreateAccessToken(string username, Permissions[] permissions)
        {
            var claims = new List<Claim>
                {
                    new Claim(JwtRegisteredClaimNames.Sub, username),
                    new Claim(JwtRegisteredClaimNames.Name, username),
                };

            claims.AddRange(permissions.Select(x => new Claim(ClaimTypes.Role, x.ToString())));

            var token = new JwtSecurityToken(
                issuer: _jwtOptions.Issuer,
                audience: _jwtOptions.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddSeconds(_jwtOptions.ExpirationSeconds),
                signingCredentials: _signingCredentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}