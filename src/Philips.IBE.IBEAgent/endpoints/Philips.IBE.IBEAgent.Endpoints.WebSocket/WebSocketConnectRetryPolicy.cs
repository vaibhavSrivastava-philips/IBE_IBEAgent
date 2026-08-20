using System.Net.WebSockets;
using Microsoft.Extensions.Logging;

namespace Philips.IBE.IBEAgent.Endpoints.WebSocket;

internal interface IWebSocketConnectRetryPolicy
{
    Task<ClientWebSocket> RentAsync(
        WebSocketConnectionPool pool,
        WebSocketOutboundOptions options,
        ILogger logger,
        bool forceFresh,
        CancellationToken cancellationToken);
}

internal sealed class WebSocketConnectRetryPolicy : IWebSocketConnectRetryPolicy
{
    public async Task<ClientWebSocket> RentAsync(
        WebSocketConnectionPool pool,
        WebSocketOutboundOptions options,
        ILogger logger,
        bool forceFresh,
        CancellationToken cancellationToken)
    {
        var attempts = Math.Max(1, options.ConnectRetryCount + 1);
        Exception? last = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await pool.RentAsync(forceFresh || attempt > 1, cancellationToken);
            }
            catch (Exception ex) when (ex is WebSocketException or IOException)
            {
                last = ex;
                if (attempt == attempts)
                    break;

                logger.LogWarning(ex,
                    "WebSocket outbound connect attempt {Attempt}/{TotalAttempts} to {Endpoint} failed. Retrying in {DelayMs} ms.",
                    attempt,
                    attempts,
                    options.Endpoint,
                    options.ConnectRetryDelay.TotalMilliseconds);

                if (options.ConnectRetryDelay > TimeSpan.Zero)
                    await Task.Delay(options.ConnectRetryDelay, cancellationToken);
            }
        }

        throw last ?? new IOException($"WebSocket outbound connect to {options.Endpoint} failed.");
    }
}
