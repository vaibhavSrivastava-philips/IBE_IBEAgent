using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.TestKit;

public sealed class FakeInboundEndpoint : IInboundEndpoint
{
    public bool Started { get; private set; }
    public bool Stopped { get; private set; }
    public Task StartAsync(CancellationToken cancellationToken) { Started = true; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken) { Stopped = true; return Task.CompletedTask; }
}