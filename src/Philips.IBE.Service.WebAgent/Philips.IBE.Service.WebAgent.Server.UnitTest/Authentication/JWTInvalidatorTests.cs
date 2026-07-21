using System;
using System.Threading;
using Philips.IBE.Service.WebAgent.Server.Authentication;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Authentication
{
    public class JWTInvalidatorTests
    {
        [Fact]
        public void AddToken_BlacklistsToken()
        {
            var invalidator = new JWTInvalidator();
            var token = "testtoken";
            var expiry = DateTime.UtcNow.AddMinutes(5);

            invalidator.AddToken(token, expiry);

            Assert.True(invalidator.IsTokenBlacklisted(token));
        }

        [Fact]
        public void IsTokenBlacklisted_ReturnsFalse_ForNonBlacklistedToken()
        {
            var invalidator = new JWTInvalidator();
            Assert.False(invalidator.IsTokenBlacklisted("notblacklisted"));
        }

        [Fact]
        public void IsTokenBlacklisted_ReturnsFalse_ForExpiredToken()
        {
            var invalidator = new JWTInvalidator();
            var token = "expiredtoken";
            var expiry = DateTime.UtcNow.AddMilliseconds(100);

            invalidator.AddToken(token, expiry);

            Thread.Sleep(200);

            Assert.False(invalidator.IsTokenBlacklisted(token));
        }

        [Fact]
        public void AddToken_OverridesExistingToken()
        {
            var invalidator = new JWTInvalidator();
            var token = "dupetoken";
            var expiry1 = DateTime.UtcNow.AddMinutes(1);
            var expiry2 = DateTime.UtcNow.AddMinutes(10);

            invalidator.AddToken(token, expiry1);
            Assert.True(invalidator.IsTokenBlacklisted(token));

            invalidator.AddToken(token, expiry2);
            Assert.True(invalidator.IsTokenBlacklisted(token));
        }
    }
}
