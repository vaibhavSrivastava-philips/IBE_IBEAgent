using System.Security.Cryptography;
using System.Text;

namespace Philips.IBE.Service.WebAgent.Server.Configuration
{
    public class JwtOptions
    {
        public required string Issuer { get; init; } 
        public required string Audience { get; init; }
        public string SigningKey { get; init; }
        public int ExpirationSeconds { get; init; }

        public JwtOptions()
        {
            SigningKey = GetHashString(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ"));

        }

        private string GetHashString(string inputString)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in GetHash(inputString))
                sb.Append(b.ToString("X2"));

            return sb.ToString();
        }

        private byte[] GetHash(string inputString)
        {
            using (HashAlgorithm algorithm = SHA256.Create())
                return algorithm.ComputeHash(Encoding.UTF8.GetBytes(inputString));
        }
    }
}
