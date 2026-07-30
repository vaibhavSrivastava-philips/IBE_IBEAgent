using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

// §6/§8 — proves AckStrategyResolver picks the right reply strategy for each configured mode,
// including the "no ack" (Acknowledgement.IsEnabled = false) fire-and-forget mode.
public sealed class AckStrategyResolverTests
{
    private static ContractOptions ContractWith(AckOptions ack, ResponseOptions? response = null, bool replyOnFilter = true) => new()
    {
        Name = "Adt",
        Inputs = [new InputOptions { InputId = 1 }],
        Acknowledgement = ack,
        Response = response ?? new ResponseOptions(),
        ReplyOnFilter = replyOnFilter,
        Outputs = [new OutputOptions { OutputId = 100 }],
    };

    [Fact]
    public void Resolves_NoAckStrategy_when_acknowledgement_disabled()
    {
        var policy = AckStrategyResolver.Resolve(ContractWith(new AckOptions { IsEnabled = false }), new ComponentRegistry());

        Assert.IsType<NoAckStrategy>(policy.Strategy);
        Assert.Equal(Timeout.InfiniteTimeSpan, policy.Timeout);
    }

    [Fact]
    public void Resolves_NormalAckStrategy_by_default()
    {
        var policy = AckStrategyResolver.Resolve(ContractWith(new AckOptions()), new ComponentRegistry());

        Assert.IsType<NormalAckStrategy>(policy.Strategy);
        Assert.Equal(Timeout.InfiniteTimeSpan, policy.Timeout);   // Normal fires on receipt, so no finite wait
    }

    [Fact]
    public void Resolves_EnhancedAckStrategy_with_a_finite_timeout()
    {
        var policy = AckStrategyResolver.Resolve(ContractWith(new AckOptions { IsEnhanced = true, TimeoutMs = 5000 }), new ComponentRegistry());

        Assert.IsType<EnhancedAckStrategy>(policy.Strategy);
        Assert.Equal(TimeSpan.FromMilliseconds(5000), policy.Timeout);   // hung required leg eventually NACKs
    }

    [Fact]
    public void Enhanced_ack_timeout_opts_out_when_non_positive()
    {
        var policy = AckStrategyResolver.Resolve(ContractWith(new AckOptions { IsEnhanced = true, TimeoutMs = 0 }), new ComponentRegistry());

        Assert.Equal(Timeout.InfiniteTimeSpan, policy.Timeout);
    }

    [Fact]
    public void Resolves_ResponseReplyStrategy_when_response_enabled_even_if_ack_disabled()
    {
        var policy = AckStrategyResolver.Resolve(
            ContractWith(new AckOptions { IsEnabled = false }, new ResponseOptions { IsEnabled = true, TimeoutMs = 1000 }),
            new ComponentRegistry());

        Assert.IsType<ResponseReplyStrategy>(policy.Strategy);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), policy.Timeout);
    }

    [Fact]
    public void ReplyOnFilter_flows_from_the_contract()
    {
        Assert.True(AckStrategyResolver.Resolve(ContractWith(new AckOptions(), replyOnFilter: true), new ComponentRegistry()).ReplyOnFilter);
        Assert.False(AckStrategyResolver.Resolve(ContractWith(new AckOptions(), replyOnFilter: false), new ComponentRegistry()).ReplyOnFilter);
    }
}
