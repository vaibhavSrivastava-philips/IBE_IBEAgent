using System.Runtime.Versioning;
using Xunit;
using Philips.IBE.Service.WebAgent.Server.Utilities;
using System.Security;
using System.Runtime.InteropServices;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Utilities
{
    [SupportedOSPlatform("windows")]
    public class DataProtectionUtilityTests
    {
        private readonly DataProtectionUtility _utility;

        public DataProtectionUtilityTests()
        {
            _utility = new DataProtectionUtility();
        }

        [Fact]
        public void ProtectValue_ReturnsEmptyString_WhenInputIsNull()
        {
            var result = _utility.ProtectValue(null);
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ProtectValue_ReturnsEmptyString_WhenInputIsEmpty()
        {
            var result = _utility.ProtectValue("");
            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void ProtectValue_ReturnsBase64String_WhenInputIsValid()
        {
            var result = _utility.ProtectValue("test123");
            Assert.False(string.IsNullOrEmpty(result));

            var bytes = Convert.FromBase64String(result);
            Assert.NotNull(bytes);
        }


        [Fact]
        public void ReadProtectedValue_ReturnsEmptySecureString_WhenInputIsNull()
        {
            var result = _utility.ReadProtectedValue(null);
            Assert.NotNull(result);
            Assert.Equal(0, result.Length);
        }

        [Fact]
        public void ReadProtectedValue_ReturnsEmptySecureString_WhenInputIsEmpty()
        {
            var result = _utility.ReadProtectedValue("");
            Assert.NotNull(result);
            Assert.Equal(0, result.Length);
        }

        [Fact]
        public void ReadProtectedValue_ReturnsSecureString_WhenInputIsValid()
        {
            var plain = "secret";
            var protectedValue = _utility.ProtectValue(plain);
            var secureString = _utility.ReadProtectedValue(protectedValue);

            Assert.NotNull(secureString);
            Assert.Equal(plain.Length, secureString.Length);

            var ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
            try
            {
                var unprotected = Marshal.PtrToStringUni(ptr);
                Assert.Equal(plain, unprotected);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }

        [Fact]
        public void ReadProtectedValue_ReturnsEmptySecureString_OnInvalidBase64()
        {
            var result = _utility.ReadProtectedValue("not_base64!");
            Assert.NotNull(result);
            Assert.Equal(0, result.Length);
        }
    }
}
