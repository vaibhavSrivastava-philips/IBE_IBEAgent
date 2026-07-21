using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Runtime.Versioning;

namespace Philips.IBE.Service.WebAgent.Server.Utilities
{
    public class DataProtectionUtility
    {
        

        public DataProtectionUtility()
        {
           
        }

        [SupportedOSPlatform("windows")]
        public SecureString ReadProtectedValue(string data)
        {
            var securePassword = new SecureString();
            if (string.IsNullOrEmpty(data))
            {
               
                return securePassword;
            }

            try
            {
                var encryptedData = Convert.FromBase64String(data);
                

                // Decrypt the data using DPAPI
                var decryptedData = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.LocalMachine);
               

                foreach (var c in Encoding.UTF8.GetChars(decryptedData))
                {
                    securePassword.AppendChar(c);
                }

                Array.Clear(decryptedData, 0, decryptedData.Length);
                securePassword.MakeReadOnly();
                
                return securePassword;
            }
            catch (Exception)
            {
                
                return securePassword;
            }
        }

        [SupportedOSPlatform("windows")]
        public virtual string ProtectValue(string data)
        {
            if (string.IsNullOrEmpty(data))
            {
               
                return string.Empty;
            }

            try
            {
                var encryptedData = ProtectedData.Protect(Encoding.UTF8.GetBytes(data), null, DataProtectionScope.LocalMachine);
               
                return Convert.ToBase64String(encryptedData);
            }
            catch (Exception)
            {
                
                return string.Empty;
            }
        }
    }
}
