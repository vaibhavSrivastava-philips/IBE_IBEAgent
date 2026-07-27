using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class DeliveryLegTests
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Delivers_and_reports_delivered()
    {
        var recording = new RecordingReplyContext();
        var ctx = MessageContextBuilder.Create(reply: recording);
        var endpoint = new FakeOutboundEndpoint();   // Delivered by default
        var leg = new DeliveryLeg(20, required: true, new BoundedInMemoryChannel(8), endpoint);

        _ = leg.RunAsync(CancellationToken.None);
        await leg.EnqueueAsync(ctx, CancellationToken.None);
        await leg.DrainAsync(DrainTimeout);

        Assert.Single(endpoint.Sent);
        var report = Assert.Single(recording.Reports);
        Assert.True(report.Required);
        Assert.Equal(DeliveryOutcome.Delivered, report.Result.Outcome);
    }

    [Fact]
    public async Task Failed_delivery_reports_failure()
    {
        var recording = new RecordingReplyContext();
        var ctx = MessageContextBuilder.Create(reply: recording);
        var endpoint = new FakeOutboundEndpoint(_ => new DeliveryResult(DeliveryOutcome.Failed, "boom"));
        var leg = new DeliveryLeg(20, required: true, new BoundedInMemoryChannel(8), endpoint);

        _ = leg.RunAsync(CancellationToken.None);
        await leg.EnqueueAsync(ctx, CancellationToken.None);
        await leg.DrainAsync(DrainTimeout);

        var report = Assert.Single(recording.Reports);
        Assert.Equal(DeliveryOutcome.Failed, report.Result.Outcome);
        Assert.Equal("boom", report.Result.Error);
    }

    [Fact]
    public async Task Endpoint_exception_is_caught_and_reported_failed()
    {
        var recording = new RecordingReplyContext();
        var ctx = MessageContextBuilder.Create(reply: recording);
        var endpoint = new FakeOutboundEndpoint(_ => throw new InvalidOperationException("kaboom"));
        var leg = new DeliveryLeg(20, required: true, new BoundedInMemoryChannel(8), endpoint);

        _ = leg.RunAsync(CancellationToken.None);
        await leg.EnqueueAsync(ctx, CancellationToken.None);
        await leg.DrainAsync(DrainTimeout);

        var report = Assert.Single(recording.Reports);
        Assert.Equal(DeliveryOutcome.Failed, report.Result.Outcome);
        Assert.Equal("kaboom", report.Result.Error);
    }

    [Fact]
    public async Task Replay_delivers_but_does_not_report()
    {
        var recording = new RecordingReplyContext();
        var ctx = MessageContextBuilder.Create(reply: recording);
        var endpoint = new FakeOutboundEndpoint();
        var leg = new DeliveryLeg(20, required: true, new BoundedInMemoryChannel(8), endpoint);

        _ = leg.RunAsync(CancellationToken.None);
        await leg.ReplayAsync(ctx, CancellationToken.None);
        await leg.DrainAsync(DrainTimeout);

        Assert.True(ctx.IsReplay);
        Assert.Single(endpoint.Sent);
        Assert.Empty(recording.Reports);   // a replay never produces a second reply
    }

    [Fact]
    public void AcceptsInput_null_or_empty_accepts_all()
    {
        var legNull = new DeliveryLeg(1, true, new BoundedInMemoryChannel(4), new FakeOutboundEndpoint());
        Assert.True(legNull.AcceptsInput(1));
        Assert.True(legNull.AcceptsInput(999));

        var legEmpty = new DeliveryLeg(1, true, new BoundedInMemoryChannel(4), new FakeOutboundEndpoint(),
            fromInputIds: new HashSet<int>());
        Assert.True(legEmpty.AcceptsInput(5));
    }

    [Fact]
    public void AcceptsInput_respects_fromInputIds()
    {
        var leg = new DeliveryLeg(1, true, new BoundedInMemoryChannel(4), new FakeOutboundEndpoint(),
            fromInputIds: new HashSet<int> { 1, 2 });

        Assert.True(leg.AcceptsInput(1));
        Assert.True(leg.AcceptsInput(2));
        Assert.False(leg.AcceptsInput(3));
    }
}
