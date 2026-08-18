using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

// RouteWhen content-based routing: a classifier stage sets facts in Headers; each leg's RouteWhen
// selects which messages it accepts. Unconditional (RouteWhen-less) legs are the catch-all; a message
// matching no leg is a filtered drop (observable). Required count reflects the routed subset per message.
public sealed class ContentRoutingContractRuntimeTests
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);

    private static MessageContext Message(RecordingReplyContext reply, string messageType)
        => MessageContextBuilder.Create(
            sourceEndpointId: 1,
            reply: reply,
            headers: new Dictionary<string, string> { ["hl7.messageType"] = messageType });

    [Fact]
    public async Task Message_routes_to_matching_leg_and_catch_all_only()
    {
        var ingress = new Dictionary<int, IMessageChannel> { [1] = new BoundedInMemoryChannel(8) };

        var epAdt = new FakeOutboundEndpoint();
        var epOru = new FakeOutboundEndpoint();
        var epAll = new FakeOutboundEndpoint();
        var legAdt = new DeliveryLeg(10, required: true, new BoundedInMemoryChannel(8), epAdt,
            routeWhen: new Dictionary<string, string> { ["hl7.messageType"] = "ADT" });
        var legOru = new DeliveryLeg(20, required: true, new BoundedInMemoryChannel(8), epOru,
            routeWhen: new Dictionary<string, string> { ["hl7.messageType"] = "ORU" });
        var legAll = new DeliveryLeg(30, required: true, new BoundedInMemoryChannel(8), epAll);   // catch-all

        var runtime = new ContractRuntime(ingress, new MessagePipeline([]), new[] { legAdt, legOru, legAll });
        _ = runtime.RunAsync(CancellationToken.None);

        var reply = new RecordingReplyContext();
        await runtime.EnqueueAsync(Message(reply, "ADT"), CancellationToken.None);
        await runtime.DrainAsync(DrainTimeout);

        Assert.Single(epAdt.Sent);                   // ADT leg matched
        Assert.Empty(epOru.Sent);                    // ORU leg did not
        Assert.Single(epAll.Sent);                   // catch-all always applies
        Assert.Equal(2, reply.ArmedRequiredTotal);   // ADT + catch-all
    }

    [Fact]
    public async Task Non_matching_message_reaches_catch_all_only()
    {
        var ingress = new Dictionary<int, IMessageChannel> { [1] = new BoundedInMemoryChannel(8) };

        var epAdt = new FakeOutboundEndpoint();
        var epAll = new FakeOutboundEndpoint();
        var legAdt = new DeliveryLeg(10, required: true, new BoundedInMemoryChannel(8), epAdt,
            routeWhen: new Dictionary<string, string> { ["hl7.messageType"] = "ADT" });
        var legAll = new DeliveryLeg(30, required: true, new BoundedInMemoryChannel(8), epAll);

        var runtime = new ContractRuntime(ingress, new MessagePipeline([]), new[] { legAdt, legAll });
        _ = runtime.RunAsync(CancellationToken.None);

        var reply = new RecordingReplyContext();
        await runtime.EnqueueAsync(Message(reply, "XYZ"), CancellationToken.None);
        await runtime.DrainAsync(DrainTimeout);

        Assert.Empty(epAdt.Sent);
        Assert.Single(epAll.Sent);
        Assert.Equal(1, reply.ArmedRequiredTotal);
        Assert.False(reply.WasFiltered);
    }

    [Fact]
    public async Task No_match_and_no_catch_all_is_filtered()
    {
        var ingress = new Dictionary<int, IMessageChannel> { [1] = new BoundedInMemoryChannel(8) };

        var epAdt = new FakeOutboundEndpoint();
        var legAdt = new DeliveryLeg(10, required: true, new BoundedInMemoryChannel(8), epAdt,
            routeWhen: new Dictionary<string, string> { ["hl7.messageType"] = "ADT" });

        var runtime = new ContractRuntime(ingress, new MessagePipeline([]), new[] { legAdt });
        _ = runtime.RunAsync(CancellationToken.None);

        var reply = new RecordingReplyContext();
        await runtime.EnqueueAsync(Message(reply, "ZZZ"), CancellationToken.None);
        await runtime.DrainAsync(DrainTimeout);

        Assert.Empty(epAdt.Sent);
        Assert.True(reply.WasFiltered);
        Assert.Equal("no route matched", reply.FilterReason);
        Assert.Null(reply.ArmedRequiredTotal);       // never armed
    }

    [Fact]
    public async Task Required_count_reflects_routed_subset()
    {
        // An OPTIONAL catch-all + a matching REQUIRED leg -> required count is 1 (only the ADT leg).
        var ingress = new Dictionary<int, IMessageChannel> { [1] = new BoundedInMemoryChannel(8) };

        var epAdt = new FakeOutboundEndpoint();
        var epAll = new FakeOutboundEndpoint();
        var legAdt = new DeliveryLeg(10, required: true, new BoundedInMemoryChannel(8), epAdt,
            routeWhen: new Dictionary<string, string> { ["hl7.messageType"] = "ADT" });
        var legAll = new DeliveryLeg(30, required: false, new BoundedInMemoryChannel(8), epAll);

        var runtime = new ContractRuntime(ingress, new MessagePipeline([]), new[] { legAdt, legAll });
        _ = runtime.RunAsync(CancellationToken.None);

        var reply = new RecordingReplyContext();
        await runtime.EnqueueAsync(Message(reply, "ADT"), CancellationToken.None);
        await runtime.DrainAsync(DrainTimeout);

        Assert.Single(epAdt.Sent);
        Assert.Single(epAll.Sent);
        Assert.Equal(1, reply.ArmedRequiredTotal);   // only legAdt is required
    }
}
