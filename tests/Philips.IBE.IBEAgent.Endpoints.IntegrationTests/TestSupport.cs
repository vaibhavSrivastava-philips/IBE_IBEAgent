// TestSupport.cs
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Philips.IBE.IBEAgent.Endpoints.Tcp;

namespace Philips.IBE.IBEAgent.Endpoints.IntegrationTests;

internal static class TestSupport
{
    public static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > timeout) throw new TimeoutException("condition was not met in time");
            await Task.Delay(20);
        }
    }

    // Reads exactly one complete MLLP frame off a stream (used to verify replies).
    public static async Task<byte[]> ReadOneFrameAsync(Stream stream, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        await foreach (var msg in MllpFramer.ReadMessagesAsync(stream, cts.Token))
            return msg;
        throw new InvalidOperationException("stream closed before a full frame arrived");
    }
}

// A read-only stream that returns at most `chunkSize` bytes per read — simulates TCP packet fragmentation.
internal sealed class ChunkedReadStream(byte[] data, int chunkSize) : Stream
{
    private int _pos;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => data.Length;
    public override long Position { get => _pos; set => throw new NotSupportedException(); }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int n = Math.Min(chunkSize, Math.Min(buffer.Length, data.Length - _pos));
        data.AsSpan(_pos, n).CopyTo(buffer.Span);
        _pos += n;
        return ValueTask.FromResult(n);   // returns 0 at end => ReadMessagesAsync completes
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = Math.Min(chunkSize, Math.Min(count, data.Length - _pos));
        Array.Copy(data, _pos, buffer, offset, n);
        _pos += n;
        return n;
    }

    public override void Flush() { }
    public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException();
    public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
}