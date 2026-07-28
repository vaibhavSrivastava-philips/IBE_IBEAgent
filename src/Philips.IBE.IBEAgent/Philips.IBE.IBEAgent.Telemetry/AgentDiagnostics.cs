using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Philips.IBE.IBEAgent.Telemetry;

// §3.10/§14 — the ONE ActivitySource + Meter for the engine (per-stage/per-leg spans; per-input
// and per-leg queue-depth gauges; store-and-forward pending/parked counters; contract.mode/leg.mode
// diagnostics). Uses the BCL System.Diagnostics.Metrics/Activity APIs directly — these ARE the
// OpenTelemetry .NET data model, so any OTel SDK the host wires up (console/OTLP exporter via
// AddOpenTelemetry().WithMetrics(b => b.AddMeter(AgentDiagnostics.MeterName))) picks these up with
// zero changes to Core/Persistence; no OTel SDK package is required to emit the data.
public static class AgentDiagnostics
{
    public const string SourceName = "Philips.IBE.IBEAgent";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    private static readonly Meter Meter = new(SourceName);

    // per-stage/per-leg delivery outcome, tagged by outputId + outcome.
    public static readonly Counter<long> Deliveries =
        Meter.CreateCounter<long>("ibe.agent.leg.deliveries", unit: "{message}", description: "Per-leg delivery outcomes.");

    public static readonly Counter<long> FilteredMessages =
        Meter.CreateCounter<long>("ibe.agent.messages.filtered", unit: "{message}", description: "Messages short-circuited by the shared pipeline.");

    // per-input / per-leg queue depth, tagged by queue name (e.g. "input:1", "leg:2").
    public static readonly UpDownCounter<long> QueueDepth =
        Meter.CreateUpDownCounter<long>("ibe.agent.queue.depth", unit: "{message}", description: "Approximate per-input/per-leg queue depth.");

    // store-and-forward buffer state, tagged by outputId.
    public static readonly Counter<long> ForwardStored =
        Meter.CreateCounter<long>("ibe.agent.forward.stored", unit: "{message}", description: "Messages stored to the forward buffer (delivery failed).");

    public static readonly Counter<long> ForwardResolved =
        Meter.CreateCounter<long>("ibe.agent.forward.resolved", unit: "{message}", description: "Forward-buffer entries resolved by a successful replay.");

    public static readonly Counter<long> ForwardParked =
        Meter.CreateCounter<long>("ibe.agent.forward.parked", unit: "{message}", description: "Forward-buffer entries parked after exceeding max attempts.");

    public static Activity? StartLegDelivery(int outputId) =>
        ActivitySource.StartActivity("leg.deliver", ActivityKind.Client)?.SetTag("leg.outputId", outputId);

    public static Activity? StartPipelineStage(string stageName) =>
        ActivitySource.StartActivity("pipeline.stage", ActivityKind.Internal)?.SetTag("stage.name", stageName);
}
