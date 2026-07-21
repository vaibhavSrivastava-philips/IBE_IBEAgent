
using Philips.IBE.Service.WebAgent.Server.Authentication;

namespace Philips.IBE.Service.WebAgent.Server.Middleware
{
    public class JWTInvalidatorMiddleware : IMiddleware
    {
        private readonly JWTInvalidator _jwtInvalidator;

        public JWTInvalidatorMiddleware(JWTInvalidator jwtInvalidator)
        {
            _jwtInvalidator = jwtInvalidator;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            var pathValue = context.Request.Path.Value;
            if (pathValue != null && !pathValue.Contains("logout"))
            {
                var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();

                if (token != null && _jwtInvalidator.IsTokenBlacklisted(token))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
            }

            await next(context);
        }
    }
}
