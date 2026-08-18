using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Security;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Persistence.IntegrationTests;

public sealed class ForwardWorkerTests : IDisposable
{
    private readonly List<string> _tempDirectories = [];

    private FileForwardStore CreateStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ibe-forward-worker-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _tempDirectories.Add(directory);
        return new FileForwardStore(directory, new NullDataProtector(), TimeSpan.FromMinutes(5));
    }

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeReplayTarget : IReplayTarget
    {
        public List<MessageContext> Replayed { get; } = [];
        public Func<MessageContext, CancellationToken, ValueTask>? Behavior { get; set; }

        public ValueTask ReplayAsync(MessageContext context, CancellationToken cancellationToken)
        {
            Replayed.Add(context);
            return Behavior?.Invoke(context, cancellationToken) ?? ValueTask.CompletedTask;
        }
    }

    private sealed class SingleTargetRegistry(int outputId, IReplayTarget target) : IReplayTargetRegistry
    {
        public bool TryGet(int candidateOutputId, out IReplayTarget? found)
        {
            if (candidateOutputId == outputId)
            {
                found = target;
                return true;
            }
            found = null;
            return false;
        }
    }

    private static ForwardWorker CreateWorker(
        IForwardStore store,
        IReplayTargetRegistry targets,
        ForwardOptions? options = null,
        ForwardWorkerHealthReporter? health = null)
        => new(store, targets, Options.Create(options ?? new ForwardOptions()), health ?? new ForwardWorkerHealthReporter(), NullLogger<ForwardWorker>.Instance);

    [Fact]
    public async Task RunOneSweepAsync_replays_a_due_entry_into_its_leg()
    {
        var store = CreateStore();
        var ctx = MessageContextBuilder.Create(payload: "hl7-message");
        await store.StoreAsync(ctx, outputId: 5, error: "transient", CancellationToken.None);

        var target = new FakeReplayTarget();
        var worker = CreateWorker(store, new SingleTargetRegistry(5, target));

        await worker.RunOneSweepAsync(CancellationToken.None);

        Assert.Single(target.Replayed);
    }

    [Fact]
    public async Task RunOneSweepAsync_parks_entries_whose_OutputId_no_longer_resolves()
    {
        var store = CreateStore();
        var ctx = MessageContextBuilder.Create(payload: "orphaned");
        await store.StoreAsync(ctx, outputId: 99, error: null, CancellationToken.None);

        var worker = CreateWorker(store, new SingleTargetRegistry(5, new FakeReplayTarget()));

        await worker.RunOneSweepAsync(CancellationToken.None);

        var due = await store.FetchDueAsync(10, CancellationToken.None);
        Assert.Empty(due); // parked, not pending/due anymore
    }

    [Fact]
    public async Task RunOneSweepAsync_reschedules_on_transient_replay_failure_below_max_attempts()
    {
        var store = CreateStore();
        var ctx = MessageContextBuilder.Create(payload: "flaky");
        await store.StoreAsync(ctx, outputId: 5, error: null, CancellationToken.None);

        var target = new FakeReplayTarget
        {
            Behavior = (_, _) => throw new InvalidOperationException("transient failure")
        };
        var options = new ForwardOptions { MaxAttempts = 5, InitialBackoffSeconds = 1 };
        var worker = CreateWorker(store, new SingleTargetRegistry(5, target), options);

        await worker.RunOneSweepAsync(CancellationToken.None);

        // Rescheduled into the future -> no longer immediately due.
        var due = await store.FetchDueAsync(10, CancellationToken.None);
        Assert.Empty(due);
    }

    [Fact]
    public async Task RunOneSweepAsync_parks_after_max_attempts_exceeded()
    {
        var store = CreateStore();
        var ctx = MessageContextBuilder.Create(payload: "poison");
        await store.StoreAsync(ctx, outputId: 5, error: null, CancellationToken.None);
        var entry = (await store.FetchDueAsync(10, CancellationToken.None)).Single();
        // Simulate the entry already having exhausted attempts.
        await store.RescheduleAsync(entry.Id, attempts: 4, DateTimeOffset.UtcNow, "prior failure", CancellationToken.None);

        var target = new FakeReplayTarget
        {
            Behavior = (_, _) => throw new InvalidOperationException("final failure")
        };
        var options = new ForwardOptions { MaxAttempts = 5, InitialBackoffSeconds = 1 };
        var worker = CreateWorker(store, new SingleTargetRegistry(5, target), options);

        await worker.RunOneSweepAsync(CancellationToken.None);

        var due = await store.FetchDueAsync(10, CancellationToken.None);
        Assert.Empty(due); // parked
    }

    [Fact]
    public void ForwardWorkerHealthReporter_defaults_to_degraded_until_worker_starts()
    {
        var health = new ForwardWorkerHealthReporter();

        var snapshot = health.GetSnapshot();

        Assert.Equal("forward-worker", snapshot.Component);
        Assert.Equal(HealthStatus.Degraded, snapshot.Status);
        Assert.Contains("not started", snapshot.Detail);
    }

    [Fact]
    public void ForwardWorkerHealthReporter_reports_started_and_stopped_states()
    {
        var health = new ForwardWorkerHealthReporter();

        health.ReportStarted(batchSize: 10, maxAttempts: 5);
        var started = health.GetSnapshot();
        health.ReportStopped();
        var stopped = health.GetSnapshot();

        Assert.Equal(HealthStatus.Healthy, started.Status);
        Assert.Contains("batch size 10", started.Detail);
        Assert.Equal(HealthStatus.Unhealthy, stopped.Status);
    }
}
