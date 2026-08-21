using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;

namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

internal interface ITcpConnectRetryPolicy
{
    Task<(TcpPooledConnection connection, bool reused)> RentAsync(
        TcpConnectionPool pool,
        TcpOutboundOptions options,
        ILogger logger,
        bool forceFresh,
        CancellationToken cancellationToken);
}

internal sealed class TcpConnectRetryPolicy : ITcpConnectRetryPolicy
{
    public async Task<(TcpPooledConnection connection, bool reused)> RentAsync(
        TcpConnectionPool pool,
        TcpOutboundOptions options,
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
            catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException)
            {
                last = ex;
                if (attempt == attempts)
                    break;

                logger.LogDebug(ex,
                    "TCP outbound connect attempt {Attempt}/{TotalAttempts} to {Host}:{Port} failed; retrying in {DelayMs} ms.",
                    attempt,
                    attempts,
                    options.Host,
                    options.Port,
                    options.ConnectRetryDelay.TotalMilliseconds);

                if (options.ConnectRetryDelay > TimeSpan.Zero)
                    await Task.Delay(options.ConnectRetryDelay, cancellationToken);
            }
        }

        throw last ?? new IOException($"TCP outbound connect to {options.Host}:{options.Port} failed.");
    }
}
