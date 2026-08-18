using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// Normal ack: reply "received" as soon as the message is accepted (on fan-out), independent of
// delivery. Renders the real ack via the source's own IAckFormatter (Format x Shape) so Normal-ack
// contracts emit proper HL7 ACKs. The neutral IBE marker is a LAST RESORT, used only when no formatter
// is registered for the source's (Format, Shape); it logs a warning (once per contract) so the missing
// formatter is visible in ops.
public sealed class NormalAckStrategy : IAckStrategy
{
    private static readonly byte[] NoFormatterAck = Encoding.UTF8.GetBytes("IBE:ACK (no ack formatter)");

    private readonly ComponentRegistry _registry;
    private readonly AckShape _shape;
    private readonly ILogger<NormalAckStrategy> _logger;
    private int _warnedNoFormatter;

    public NormalAckStrategy(ComponentRegistry registry, AckShape shape, ILogger<NormalAckStrategy>? logger = null)
    {
        _registry = registry;
        _shape = shape;
        _logger = logger ?? NullLogger<NormalAckStrategy>.Instance;
    }

    public bool RepliesOnReceipt => true;   // fire on receipt, not after delivery

    public Task WriteReplyAsync(MessageContext context, ReplyOutcome outcome)
    {
        var result = new DeliveryResult(outcome.Outcome, outcome.Reason);
        if (_registry.TryGetAckFormatter(context.Format, _shape, out var formatter) && formatter is not null)
            return context.Ack.WriteAsync(formatter.Render(context, result), CancellationToken.None);

        if (Interlocked.Exchange(ref _warnedNoFormatter, 1) == 0)   // warn once per contract, not per message
            _logger.LogWarning(
                "No ack formatter registered for (Format {Format}, Shape {Shape}); source {SourceEndpointId} receives a neutral IBE placeholder reply instead of a generated ack.",
                context.Format, _shape, context.SourceEndpointId);
        return context.Ack.WriteAsync(NoFormatterAck, CancellationToken.None);
    }
}