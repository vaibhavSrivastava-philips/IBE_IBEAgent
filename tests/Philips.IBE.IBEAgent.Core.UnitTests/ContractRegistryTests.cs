using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class ContractRegistryTests
{
    [Fact]
    public void ForSource_returns_registered_runtime()
    {
        var registry = new ContractRegistry();
        var runtime = new FakeContractRuntime();

        registry.Register(runtime, [1, 2]);

        Assert.Same(runtime, registry.ForSource(1));
        Assert.Same(runtime, registry.ForSource(2));
    }

    [Fact]
    public void ForSource_throws_when_input_not_registered()
    {
        var registry = new ContractRegistry();
        Assert.Throws<KeyNotFoundException>(() => registry.ForSource(99));
    }

    [Fact]
    public void Register_throws_when_input_already_claimed()
    {
        var registry = new ContractRegistry();
        registry.Register(new FakeContractRuntime(), [1]);

        Assert.Throws<InvalidOperationException>(() => registry.Register(new FakeContractRuntime(), [1]));
    }

    [Fact]
    public void TryGetForSource_returns_false_when_missing()
    {
        var registry = new ContractRegistry();
        Assert.False(registry.TryGetForSource(5, out var runtime));
        Assert.Null(runtime);
    }

    private sealed class FakeContractRuntime : IContractRuntime
    {
        public ValueTask EnqueueAsync(MessageContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DrainAsync(TimeSpan timeout) => Task.CompletedTask;
    }
}
