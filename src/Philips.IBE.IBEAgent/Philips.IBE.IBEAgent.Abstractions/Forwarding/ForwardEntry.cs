namespace Philips.IBE.IBEAgent.Abstractions;


public sealed record ForwardEntry(
    long Id,
    ReadOnlyMemory<byte> Message,          // post-pipeline canonical payload + header snapshot for THIS leg (INV-5)
    int OutputId,
    ForwardStatus Status,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    string? LastError,
    DateTimeOffset CreatedAt);