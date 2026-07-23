using System.Buffers;
using System.Runtime.CompilerServices;

namespace Philips.IBE.IBEAgent.Endpoints.Tcp;

internal static class MllpFramer
{
    public static byte[] Frame(ReadOnlySpan<byte> payload)
    {
        var buffer = new byte[payload.Length + 3];
        buffer[0] = Mllp.StartBlock;
        payload.CopyTo(buffer.AsSpan(1));
        buffer[^2] = Mllp.EndBlock1;
        buffer[^1] = Mllp.EndBlock2;
        return buffer;
    }

    // Yields each complete MLLP message payload (framing bytes stripped) until the stream closes/cancels.
    public static async IAsyncEnumerable<byte[]> ReadMessagesAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken ct)
    {
        var acc = new ArrayBufferWriter<byte>();
        var read = new byte[8192];
        bool inMessage = false, sawFs = false;

        while (!ct.IsCancellationRequested)
        {
            int n = await stream.ReadAsync(read, ct);
            if (n == 0) yield break;                 // peer closed

            for (int i = 0; i < n; i++)
            {
                byte b = read[i];
                if (!inMessage)
                {
                    if (b == Mllp.StartBlock) { inMessage = true; sawFs = false; acc.Clear(); }
                    continue;                        // ignore bytes before a start block
                }
                if (sawFs)
                {
                    if (b == Mllp.EndBlock2)         // FS + CR => end of message
                    {
                        yield return acc.WrittenSpan.ToArray();
                        inMessage = false; sawFs = false;
                    }
                    else { Append(acc, Mllp.EndBlock1); Append(acc, b); sawFs = false; }
                    continue;
                }
                if (b == Mllp.EndBlock1) { sawFs = true; continue; }
                Append(acc, b);
            }
        }

        static void Append(ArrayBufferWriter<byte> w, byte b)
        {
            var span = w.GetSpan(1); span[0] = b; w.Advance(1);
        }
    }
}