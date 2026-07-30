using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

// §4/§14 Phase 4 — proves the fan-out mechanism scales to N inputs -> M outputs with a
// pass-through (no-op) shared pipeline: every input's message reaches every applicable
// (unfiltered) leg, each leg is armed with the correct per-message required count, and legs
// scoped to a subset of inputs (FromInputIds) only ever see their own inputs' messages.
public sealed class ManyToManyContractRuntimeTests
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Every_input_fans_out_to_every_unscoped_leg()
    {
        const int inputCount = 3;
        const int outputCount = 4;

        var ingress = new Dictionary<int, IMessageChannel>();
        for (var i = 1; i <= inputCount; i++)
            ingress[i] = new BoundedInMemoryChannel(16);

        var endpoints = new List<FakeOutboundEndpoint>();
        var legs = new List<DeliveryLeg>();
        for (var o = 1; o <= outputCount; o++)
        {
            var endpoint = new FakeOutboundEndpoint();
            endpoints.Add(endpoint);
            legs.Add(new DeliveryLeg(o, required: true, new BoundedInMemoryChannel(16), endpoint));
        }

        var runtime = new ContractRuntime(ingress, new MessagePipeline([]), legs);
        _ = runtime.RunAsync(CancellationToken.None);

        var replies = new List<RecordingReplyContext>();
        for (var i = 1; i <= inputCount; i++)
        {
            var reply = new RecordingReplyContext();
            replies.Add(reply);
            var ctx = MessageContextBuilder.Create(sourceEndpointId: i, reply: reply);
            await runtime.EnqueueAsync(ctx, CancellationToken.None);
        }

        await runtime.DrainAsync(DrainTimeout);

        // every leg received exactly one message per input (unscoped legs accept all inputs)
        foreach (var endpoint in endpoints)
            Assert.Equal(inputCount, endpoint.Sent.Count);

        // every input's message was armed with, and got a positive reply for, all M outputs
        foreach (var reply in replies)
        {
            Assert.Equal(outputCount, reply.ArmedRequiredTotal);
            Assert.Equal(outputCount, reply.Reports.Count);
            Assert.All(reply.Reports, r => Assert.Equal(DeliveryOutcome.Delivered, r.Result.Outcome));
        }
    }

    [Fact]
    public async Task Scoped_legs_only_receive_their_own_inputs_in_a_many_to_many_fan_out()
    {
        // 3 inputs -> 2 legs: legA scoped to inputs {1,2}, legB scoped to input {3} only.
        var ingress = new Dictionary<int, IMessageChannel>
        {
            [1] = new BoundedInMemoryChannel(8),
            [2] = new BoundedInMemoryChannel(8),
            [3] = new BoundedInMemoryChannel(8),
        };

        var epA = new FakeOutboundEndpoint();
        var epB = new FakeOutboundEndpoint();
        var legA = new DeliveryLeg(10, required: true, new BoundedInMemoryChannel(8), epA,
            fromInputIds: new HashSet<int> { 1, 2 });
        var legB = new DeliveryLeg(20, required: true, new BoundedInMemoryChannel(8), epB,
            fromInputIds: new HashSet<int> { 3 });

        var runtime = new ContractRuntime(ingress, new MessagePipeline([]), new[] { legA, legB });
        _ = runtime.RunAsync(CancellationToken.None);

        var replyFrom1 = new RecordingReplyContext();
        var replyFrom2 = new RecordingReplyContext();
        var replyFrom3 = new RecordingReplyContext();

        await runtime.EnqueueAsync(MessageContextBuilder.Create(sourceEndpointId: 1, reply: replyFrom1), CancellationToken.None);
        await runtime.EnqueueAsync(MessageContextBuilder.Create(sourceEndpointId: 2, reply: replyFrom2), CancellationToken.None);
        await runtime.EnqueueAsync(MessageContextBuilder.Create(sourceEndpointId: 3, reply: replyFrom3), CancellationToken.None);

        await runtime.DrainAsync(DrainTimeout);

        Assert.Equal(2, epA.Sent.Count);   // inputs 1 and 2 both reached legA
        Assert.Single(epB.Sent);           // only input 3 reached legB

        Assert.Equal(1, replyFrom1.ArmedRequiredTotal);   // only legA applies to input 1
        Assert.Equal(1, replyFrom2.ArmedRequiredTotal);   // only legA applies to input 2
        Assert.Equal(1, replyFrom3.ArmedRequiredTotal);   // only legB applies to input 3
    }
}
