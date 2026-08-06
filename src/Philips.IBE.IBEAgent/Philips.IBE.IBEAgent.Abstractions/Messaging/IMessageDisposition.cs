namespace Philips.IBE.IBEAgent.Abstractions;

// Source-side completion of a received message, fired ONCE when the message settles (all required legs
// done / filtered / timed out). Distinct from IAckToken (which writes reply BYTES): this disposes the
// SOURCE artifact — e.g. a File source moves/marks the consumed file. No-op transports pass null.
public interface IMessageDisposition
{
    ValueTask CompleteAsync(MessageCompletion outcome, CancellationToken cancellationToken);
}
