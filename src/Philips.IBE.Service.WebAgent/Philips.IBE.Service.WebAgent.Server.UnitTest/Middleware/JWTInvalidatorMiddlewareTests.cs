using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Philips.IBE.Service.WebAgent.Server.Authentication;
using Philips.IBE.Service.WebAgent.Server.Middleware;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Middleware
{
    public class TestJWTInvalidator : JWTInvalidator
    {
        private readonly Func<string, bool> _isTokenBlacklistedFunc;

        public TestJWTInvalidator(Func<string, bool> isTokenBlacklistedFunc)
        {
            _isTokenBlacklistedFunc = isTokenBlacklistedFunc;
        }

        public new bool IsTokenBlacklisted(string token)
        {
            return _isTokenBlacklistedFunc(token);
        }
    }

    public class JWTInvalidatorMiddlewareTests
    {
        [Fact]
        public async Task Invoke_Allows_Request_When_Token_Is_Valid()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["Authorization"] = "Bearer validtoken";
            var nextCalled = false;
            RequestDelegate next = (ctx) =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var jwtInvalidator = new TestJWTInvalidator(token => false);
            var middleware = new JWTInvalidatorMiddleware(jwtInvalidator);

            await middleware.InvokeAsync(httpContext, next);

            Assert.True(nextCalled);
        }



        [Fact]
        public async Task Invoke_Skips_When_No_Authorization_Header()
        {
            var httpContext = new DefaultHttpContext();
            var nextCalled = false;
            RequestDelegate next = (ctx) =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var jwtInvalidator = new TestJWTInvalidator(token => false);
            var middleware = new JWTInvalidatorMiddleware(jwtInvalidator);

            await middleware.InvokeAsync(httpContext, next);

            Assert.True(nextCalled);
        }
    }
}
