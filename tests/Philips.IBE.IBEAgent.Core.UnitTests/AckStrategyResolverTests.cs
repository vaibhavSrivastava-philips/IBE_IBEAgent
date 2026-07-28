using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

// §6/§8 — proves AckStrategyResolver picks the right reply strategy for each configured mode,
// including the "no ack" (Acknowledgement.IsEnabled = false) fire-and-forget mode.
public sealed class AckStrategyResolverTests
{
    private static ContractOptions ContractWith(AckOptions ack, ResponseOptions? response = null) => new()
    {
        Name = "Adt",
        Inputs = [new InputOptions { InputId = 1 }],
        Acknowledgement = ack,
        Response = response ?? new ResponseOptions(),
        Outputs = [new OutputOptions { OutputId = 100 }],
    };

    [Fact]
    public void Resolves_NoAckStrategy_when_acknowledgement_disabled()
    {
        var contract = ContractWith(new AckOptions { IsEnabled = false });

        var (strategy, timeout) = AckStrategyResolver.Resolve(contract, new ComponentRegistry());

        Assert.IsType<NoAckStrategy>(strategy);
        Assert.Equal(Timeout.InfiniteTimeSpan, timeout);
    }

    [Fact]
    public void Resolves_NormalAckStrategy_by_default()
    {
        var contract = ContractWith(new AckOptions());

        var (strategy, _) = AckStrategyResolver.Resolve(contract, new ComponentRegistry());

        Assert.IsType<NormalAckStrategy>(strategy);
    }

    [Fact]
    public void Resolves_EnhancedAckStrategy_when_configured()
    {
        var contract = ContractWith(new AckOptions { IsEnhanced = true });

        var (strategy, _) = AckStrategyResolver.Resolve(contract, new ComponentRegistry());

        Assert.IsType<EnhancedAckStrategy>(strategy);
    }

    [Fact]
    public void Resolves_ResponseReplyStrategy_when_response_enabled_even_if_ack_disabled()
    {
        var contract = ContractWith(
            new AckOptions { IsEnabled = false },
            new ResponseOptions { IsEnabled = true, TimeoutMs = 1000 });

        var (strategy, timeout) = AckStrategyResolver.Resolve(contract, new ComponentRegistry());

        Assert.IsType<ResponseReplyStrategy>(strategy);
        Assert.Equal(TimeSpan.FromMilliseconds(1000), timeout);
    }
}
