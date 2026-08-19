using Microsoft.Extensions.Logging;

namespace Philips.IBE.IBEAgent.Endpoints.Http;

internal interface IHttpSendRetryPolicy
{
    Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
        HttpOutboundOptions options,
        ILogger logger,
        CancellationToken cancellationToken);
}

internal sealed class HttpSendRetryPolicy : IHttpSendRetryPolicy
{
    public async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
        HttpOutboundOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var attempts = Math.Max(1, options.ConnectRetryCount + 1);
        Exception? last = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await sendAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                last = ex;
                if (attempt == attempts)
                    break;

                logger.LogWarning(ex,
                    "HTTP outbound attempt {Attempt}/{TotalAttempts} to {Endpoint} failed. Retrying in {DelayMs} ms.",
                    attempt,
                    attempts,
                    options.Endpoint,
                    options.ConnectRetryDelay.TotalMilliseconds);

                if (options.ConnectRetryDelay > TimeSpan.Zero)
                    await Task.Delay(options.ConnectRetryDelay, cancellationToken);
            }
        }

        throw last ?? new IOException($"HTTP outbound send to {options.Endpoint} failed.");
    }
}
