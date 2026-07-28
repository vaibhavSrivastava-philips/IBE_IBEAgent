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
    private readonly ILogger<ForwardWorker> _logger;

    public ForwardWorker(
        IForwardStore store,
        IReplayTargetRegistry targets,
        IOptions<ForwardOptions> options,
        ILogger<ForwardWorker> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        _options = options?.Value ?? new ForwardOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOneSweepAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "ForwardWorker sweep failed; will retry next interval.");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { }
        }
    }

    // Public for direct testability (a single sweep without waiting on the poll interval); the
    // BackgroundService loop above is a thin driver around this.
    public async Task RunOneSweepAsync(CancellationToken cancellationToken)
    {
        var due = await _store.FetchDueAsync(_options.FetchBatchSize, cancellationToken);
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
            await target.ReplayAsync(context, cancellationToken);
            // Delivery/resolve happens inside the leg's own consumer loop (DeliveryLeg.ConsumeAsync
            // calls IForwardStore.ResolveAsync on success) — the worker only re-enqueues here.
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
            await _store.ParkAsync(entry.Id, $"Max attempts ({_options.MaxAttempts}) exceeded: {error}", cancellationToken);
            return;
        }

        var delay = _options.Backoff == BackoffKind.Exponential
            ? TimeSpan.FromSeconds(_options.InitialBackoffSeconds * Math.Pow(2, attempts - 1))
            : TimeSpan.FromSeconds(_options.InitialBackoffSeconds);

        await _store.RescheduleAsync(entry.Id, attempts, DateTimeOffset.UtcNow.Add(delay), error, cancellationToken);
    }
}
