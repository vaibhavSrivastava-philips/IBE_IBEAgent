using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Endpoints.File;

// The source side of a polled file message. File has NO reply channel (WriteAsync is a no-op); the
// source "completion" is a disposition, applied at settle by the endpoint's FileDisposition
// (move to processed/error, advance a watermark, or delete). Settle-once.
public sealed class FileSourceToken : IAckToken, IMessageDisposition
{
    private readonly string _sourcePath;
    private readonly DateTime _effectiveTimeUtc;
    private readonly FileDisposition _disposition;
    private readonly Action _onCompleted;
    private int _completed;

    public FileSourceToken(string sourcePath, DateTime effectiveTimeUtc, FileDisposition disposition, Action onCompleted)
    {
        _sourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
        _effectiveTimeUtc = effectiveTimeUtc;
        _disposition = disposition ?? throw new ArgumentNullException(nameof(disposition));
        _onCompleted = onCompleted ?? throw new ArgumentNullException(nameof(onCompleted));
    }

    // File has no reply channel — a NoAck source. Kept so the source side is a single handle.
    public Task WriteAsync(ReadOnlyMemory<byte> reply, CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask CompleteAsync(MessageCompletion outcome, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;   // settle once
        try
        {
            await _disposition.ApplyAsync(_sourcePath, _effectiveTimeUtc, outcome, cancellationToken);
        }
        finally
        {
            _onCompleted();   // release the in-flight guard even if the disposition failed
        }
    }
}
