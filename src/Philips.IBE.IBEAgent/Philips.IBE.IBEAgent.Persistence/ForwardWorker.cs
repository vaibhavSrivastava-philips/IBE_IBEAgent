using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;

namespace Philips.IBE.IBEAgent.Persistence;

// §3.9 — the ONE always-on retry loop, hostable either in-process (co-located with the agent)
// or out-of-process (Philips.IBE.IBEAgent.ForwardService); the "Ibe:Forward:Owner" config value
// only changes composition, never this class. Replays go straight into the failed leg's own
// IReplayTarget (DeliveryLeg.ReplayAsync in-process) — never through the Dispatcher, never
// re-routed, never re-processed, never re-acked (the reply was already settled).
public sealed class ForwardWorker : BackgroundService
{
    private readonly IForwardStore _store;
    private readonly IReplayTargetRegistry _targets;
    private readonly ForwardOptions _options;
    private readonly ForwardWorkerHealthReporter _health;
    private readonly ILogger<ForwardWorker> _logger;

    public ForwardWorker(
        IForwardStore store,
        IReplayTargetRegistry targets,
        IOptions<ForwardOptions> options,
        ForwardWorkerHealthReporter health,
        ILogger<ForwardWorker> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (options?.Value is { } configured)
        {
            _options = configured;
        }
        else
        {
            // Forward config is optional; run on defaults but make the substitution visible in ops.
            _options = new ForwardOptions();
            _logger.LogWarning(
                "No Forward configuration found; using defaults (PollIntervalSeconds {PollIntervalSeconds}, MaxAttempts {MaxAttempts}).",
                _options.PollIntervalSeconds, _options.MaxAttempts);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        _logger.LogInformation(
            "ForwardWorker started (poll interval {IntervalSeconds}s, batch size {BatchSize}, max attempts {MaxAttempts}).",
            interval.TotalSeconds, _options.FetchBatchSize, _options.MaxAttempts);
        _health.ReportStarted(_options.FetchBatchSize, _options.MaxAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOneSweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _health.ReportSweepFailure(ex.GetType().Name);
                _logger.LogError(ex, "ForwardWorker sweep failed; will retry next interval.");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { }
        }

        _health.ReportStopped();
        _logger.LogInformation("ForwardWorker stopped.");
    }

    // Public for direct testability (a single sweep without waiting on the poll interval); the
    // BackgroundService loop above is a thin driver around this.
    public async Task RunOneSweepAsync(CancellationToken cancellationToken)
    {
        var due = await _store.FetchDueAsync(_options.FetchBatchSize, cancellationToken);
        if (due.Count > 0)
            _logger.LogDebug("Forward sweep: {DueCount} entr(ies) due for replay.", due.Count);

        foreach (var entry in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReplayOneAsync(entry, cancellationToken);
        }
    }

    private async Task ReplayOneAsync(ForwardEntry entry, CancellationToken cancellationToken)
    {
        // Config drift (§3.9 edge case): an OutputId that no longer resolves is parked with a
        // reason, never fatal to the worker.
        if (!_targets.TryGet(entry.OutputId, out var target) || target is null)
        {
            _logger.LogWarning(
                "Forward entry {EntryId} parked: output {OutputId} no longer resolves to a leg (config drift).",
                entry.Id, entry.OutputId);
            await _store.ParkAsync(entry.Id, $"OutputId {entry.OutputId} no longer resolves to a leg.", cancellationToken);
            return;
        }

        ReplayEnvelope envelope;
        try
        {
            envelope = ReplayEnvelope.FromPlaintext(entry.Message.ToArray());
        }
        catch (Exception ex)
        {
            // Log the failure class, not the raw exception: a deserialization error can embed the
            // (decrypted) message content (PHI) in its message.
            _logger.LogWarning(
                "Forward entry {EntryId} parked: corrupt forward-store entry ({ParseError}).",
                entry.Id, ex.GetType().Name);
            await _store.ParkAsync(entry.Id, $"Corrupt forward-store entry: {ex.Message}", cancellationToken);
            return;
        }

        var context = new MessageContext(
            envelope.CorrelationId,
            envelope.SourceEndpointId,
            envelope.Format,
            NoOpAckToken.Instance,
            NoOpReplyContext.Instance,
            envelope.Payload,
            envelope.Headers);

        try
        {
            await target.ReplayAsync(context, cancellationToken); // delivers straight through the leg's endpoint; throws on failure
            await _store.ResolveAsync(entry.Id, cancellationToken);  // delivered -> clear the entry (the worker owns resolve/reschedule/park)
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await RescheduleOrParkAsync(entry, ex.Message, cancellationToken);
        }
    }

    private async Task RescheduleOrParkAsync(ForwardEntry entry, string error, CancellationToken cancellationToken)
    {
        var attempts = entry.Attempts + 1;
        if (attempts >= _options.MaxAttempts)
        {
            _logger.LogWarning(
                "Forward entry {EntryId} (output {OutputId}) parked after {Attempts} attempt(s): {Error}",
                entry.Id, entry.OutputId, attempts, error);
            await _store.ParkAsync(entry.Id, $"Max attempts ({_options.MaxAttempts}) exceeded: {error}", cancellationToken);
            return;
        }

        var delay = _options.Backoff == BackoffKind.Exponential
            ? TimeSpan.FromSeconds(_options.InitialBackoffSeconds * Math.Pow(2, attempts - 1))
            : TimeSpan.FromSeconds(_options.InitialBackoffSeconds);

        _logger.LogDebug(
            "Forward entry {EntryId} (output {OutputId}) rescheduled (attempt {Attempts}, retry in {DelaySeconds}s): {Error}",
            entry.Id, entry.OutputId, attempts, delay.TotalSeconds, error);
        await _store.RescheduleAsync(entry.Id, attempts, DateTimeOffset.UtcNow.Add(delay), error, cancellationToken);
    }
}
