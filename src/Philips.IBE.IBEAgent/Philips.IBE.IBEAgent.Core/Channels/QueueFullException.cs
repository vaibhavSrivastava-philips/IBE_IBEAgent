namespace Philips.IBE.IBEAgent.Core;

// Thrown by a Reject-policy channel when full. An HTTP inbound endpoint can map this to 503.
// NOTE: if endpoints must catch this without referencing Core, move it to Abstractions.
public sealed class QueueFullException : Exception
{
    public QueueFullException() { }
    public QueueFullException(string message) : base(message) { }
    public QueueFullException(string message, Exception innerException) : base(message, innerException) { }
}