namespace Philips.IBE.IBEAgent.Configuration;

// §8 — developer-owned per-leg encoding bundle ("how a leg renders a message to wire bytes"):
// a message codec plus an optional batch codec, both named catalog Codecs entries. Referenced by
// Template.Format or Output.Format. Pure "plug-and-play code" concern — FSEs pick it by name and
// never choose raw codecs. Distinct from an inbound endpoint's Format tag (the parser/message type).
public sealed record OutputFormatOptions
{
    public string? Codec { get; init; }        // names a catalog Codecs entry (IMessageCodec, one message -> bytes)
    public string? BatchCodec { get; init; }   // names a catalog Codecs entry (IBatchCodec, N -> 1); used when FSE enables batching
}
