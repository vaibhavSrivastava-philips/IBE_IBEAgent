namespace Philips.IBE.IBEAgent.Abstractions;

public readonly record struct DeliveryResult(
    DeliveryOutcome Outcome,
    string? Error = null,
    ReadOnlyMemory<byte> ResponsePayload = default,
    string? ResponseFormat = null);