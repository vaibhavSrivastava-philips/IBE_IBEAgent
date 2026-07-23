using System.Net;
using Philips.IBE.IBEAgent.Abstractions;
namespace Philips.IBE.IBEAgent.Endpoints.Http;

internal sealed class HttpResponseAckToken(HttpListenerResponse response) : IAckToken
{
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _written;

    public Task Completion => _completed.Task;

    public async Task WriteAsync(ReadOnlyMemory<byte> reply, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _written, 1) != 0) return;   // one-shot: ignore late/second reply
        try
        {
            response.StatusCode = reply.IsEmpty ? 204 : 200;
            if (!reply.IsEmpty)
            {
                response.ContentLength64 = reply.Length;
                await response.OutputStream.WriteAsync(reply, cancellationToken);
            }
        }
        finally { response.Close(); _completed.TrySetResult(); }
    }

    public void CompleteWithError(int statusCode)                  // called on timeout/failure to release the request
    {
        if (Interlocked.Exchange(ref _written, 1) != 0) return;
        try { response.StatusCode = statusCode; }
        finally { response.Close(); _completed.TrySetResult(); }
    }
}