using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Formats.Hl7.Filtering;

public sealed class Hl7FilterStage : IMessageStage
{
    public const string Name = "hl7-filter";
    public const string FilterReasonHeader = "hl7.filterReason";

    private readonly Hl7FilterOptions _options;
    private readonly ILogger<Hl7FilterStage> _logger;

    public Hl7FilterStage(Hl7FilterOptions? options = null, ILogger<Hl7FilterStage>? logger = null)
    {
        _options = options ?? new Hl7FilterOptions();
        _logger = logger ?? NullLogger<Hl7FilterStage>.Instance;
    }

    public Task<StageResult> ProcessAsync(MessageContext context)
    {
        var reader = Hl7MessageReader.Parse(context.Payload);
        var messageType = reader.MessageType;

        if (_options.BlockedMessageTypes.Count > 0 && messageType is not null && Contains(_options.BlockedMessageTypes, messageType))
            return FilterAsync(context, $"HL7 message type '{messageType}' is blocked.");

        if (_options.AllowedMessageTypes.Count > 0 && (messageType is null || !Contains(_options.AllowedMessageTypes, messageType)))
            return FilterAsync(context, $"HL7 message type '{messageType ?? "unknown"}' is not allowed.");

        foreach (var rule in _options.FieldRules)
        {
            var value = reader.Field(rule.Segment, rule.Field, rule.Occurrence);
            if (rule.EqualsValue is not null && string.Equals(value, rule.EqualsValue, StringComparison.Ordinal))
                return FilterAsync(context, rule.Reason ?? $"HL7 {rule.Segment}-{rule.Field} matched blocked value.");

            if (rule.NotEqualsValue is not null && !string.Equals(value, rule.NotEqualsValue, StringComparison.Ordinal))
                return FilterAsync(context, rule.Reason ?? $"HL7 {rule.Segment}-{rule.Field} did not match required value.");
        }

        return Task.FromResult(StageResult.Continue);
    }

    private Task<StageResult> FilterAsync(MessageContext context, string reason)
    {
        context.Headers[FilterReasonHeader] = reason;
        _logger.LogInformation(
            "HL7 message filtered: {Reason} (correlation {CorrelationId}, source {SourceEndpointId}).",
            reason, context.CorrelationId, context.SourceEndpointId);
        return Task.FromResult(StageResult.Filter(reason));
    }

    private static bool Contains(IReadOnlyList<string> values, string value)
        => values.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
}
