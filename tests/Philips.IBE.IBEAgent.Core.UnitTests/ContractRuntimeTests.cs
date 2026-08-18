using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class ContractRuntimeTests
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Delivers_to_leg_and_arms_required_count()
    {
        var recording = new RecordingReplyContext();
        var ctx = MessageContextBuilder.Create(sourceEndpointId: 1, reply: recording);

        var endpoint = new FakeOutboundEndpoint();
        var leg = new DeliveryLeg(10, required: true, new BoundedInMemoryChannel(8), endpoint);
        var ingress = new Dictionary<int, IMessageChannel> { [1] = new BoundedInMemoryChannel(8) };
        var runtime = new ContractRuntime(ingress, new MessagePipeline([]), new[] { leg });

        _ = runtime.RunAsync(CancellationToken.None);
        await runtime.EnqueueAsync(ctx, CancellationToken.None);
        await runtime.DrainAsync(DrainTimeout);

        Assert.Equal(1, recording.ArmedRequiredTotal);
        Assert.Single(endpoint.Sent);
        var report = Assert.Single(recording.Reports);
        Assert.Equal(DeliveryOutcome.Delivered, report.Result.Outcome);
    }

    [Fact]
    public async Task Filtered_pipeline_reports_filtered_and_skips_legs()
    {
        var recording = new RecordingReplyContext();
        var ctx = MessageContextBuilder.Create(sourceEndpointId: 1, reply: recording);

        var endpoint = new FakeOutboundEndpoint();
        var leg = new DeliveryLeg(10, required: true, new BoundedInMemoryChannel(8), endpoint);
        var ingress = new Dictionary<int, IMessageChannel> { [1] = new BoundedInMemoryChannel(8) };
        var runtime = new ContractRuntime(ingress, new FilteringPipeline(), new[] { leg });

        _ = runtime.RunAsync(CancellationToken.None);
        await runtime.EnqueueAsync(ctx, CancellationToken.None);
        await runtime.DrainAsync(DrainTimeout);

        Assert.True(recording.WasFiltered);
        Assert.Null(recording.ArmedRequiredTotal);   // OnFannedOut never called
        Assert.Empty(endpoint.Sent);
    }

    [Fact]
    public async Task Filtered_pipeline_preserves_filter_reason_for_reply_strategy()
    {
        var recording = new RecordingReplyContext();
        var ctx = MessageContextBuilder.Create(sourceEndpointId: 1, reply: recording);

        var endpoint = new FakeOutboundEndpoint();
        var leg = new DeliveryLeg(10, required: true, new BoundedInMemoryChannel(8), endpoint);
        var ingress = new Dictionary<int, IMessageChannel> { [1] = new BoundedInMemoryChannel(8) };
        var runtime = new ContractRuntime(ingress, new FilteringPipeline("hl7-filter: ADT blocked"), new[] { leg });

        _ = runtime.RunAsync(CancellationToken.None);
        await runtime.EnqueueAsync(ctx, CancellationToken.None);
        await runtime.DrainAsync(DrainTimeout);

        Assert.True(recording.WasFiltered);
        Assert.Equal("hl7-filter: ADT blocked", recording.FilterReason);
        Assert.Empty(endpoint.Sent);
    }

    [Fact]
    public async Task Fans_out_only_to_legs_that_accept_the_source()
    {
        var recording = new RecordingReplyContext();
        var ctx = MessageContextBuilder.Create(sourceEndpointId: 1, reply: recording);

        var epA = new FakeOutboundEndpoint();
        var epB = new FakeOutboundEndpoint();
        var legA = new DeliveryLeg(10, true, new BoundedInMemoryChannel(8), epA, fromInputIds: new HashSet<int> { 1 });
        var legB = new DeliveryLeg(20, true, new BoundedInMemoryChannel(8), epB, fromInputIds: new HashSet<int> { 2 });
        var ingress = new Dictionary<int, IMessageChannel> { [1] = new BoundedInMemoryChannel(8) };
        var runtime = new ContractRuntime(ingress, new MessagePipeline([]), new[] { legA, legB });

        _ = runtime.RunAsync(CancellationToken.None);
        await runtime.EnqueueAsync(ctx, CancellationToken.None);
        await runtime.DrainAsync(DrainTimeout);

        Assert.Equal(1, recording.ArmedRequiredTotal);   // only legA is applicable+required for source 1
        Assert.Single(epA.Sent);
        Assert.Empty(epB.Sent);
    }

    private sealed class FilteringPipeline(string reason = "blocked") : IMessagePipeline
    {
        public ValueTask<PipelineResult> ExecuteAsync(MessageContext context)
            => new(PipelineResult.Filtered(reason));
    }
}
