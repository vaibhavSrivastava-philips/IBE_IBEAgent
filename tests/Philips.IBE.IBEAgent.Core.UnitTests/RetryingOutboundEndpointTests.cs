using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Configuration;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class RetryingOutboundEndpointTests
{
    // BackoffSeconds=0 keeps the retries immediate so tests never wait on wall-clock backoff.
    private static RetryOptions Retry(int maxAttempts, BackoffKind backoff = BackoffKind.Fixed, int backoffSeconds = 0)
        => new() { MaxAttempts = maxAttempts, BackoffSeconds = backoffSeconds, Backoff = backoff };

    [Fact]
    public async Task Retries_a_failed_delivery_until_it_succeeds()
    {
        var calls = 0;
        var inner = new FakeOutboundEndpoint(_ => ++calls < 3
            ? new DeliveryResult(DeliveryOutcome.Failed, "transient")
            : new DeliveryResult(DeliveryOutcome.Delivered));
        var endpoint = new RetryingOutboundEndpoint(inner, Retry(3));

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Returns_the_failed_result_after_exhausting_attempts()
    {
        var calls = 0;
        var inner = new FakeOutboundEndpoint(_ => { calls++; return new DeliveryResult(DeliveryOutcome.Failed, "down"); });
        var endpoint = new RetryingOutboundEndpoint(inner, Retry(3));

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Failed, result.Outcome);
        Assert.Equal("down", result.Error);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Retries_a_thrown_transport_exception_until_it_succeeds()
    {
        var calls = 0;
        var inner = new FakeOutboundEndpoint(_ =>
        {
            if (++calls < 2) throw new IOException("stale connection");
            return new DeliveryResult(DeliveryOutcome.Delivered);
        });
        var endpoint = new RetryingOutboundEndpoint(inner, Retry(3));

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Delivered, result.Outcome);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Propagates_a_thrown_exception_on_the_final_attempt()
    {
        var calls = 0;
        var inner = new FakeOutboundEndpoint(_ => { calls++; throw new IOException("down"); });
        var endpoint = new RetryingOutboundEndpoint(inner, Retry(3));

        await Assert.ThrowsAsync<IOException>(
            () => endpoint.SendAsync(MessageContextBuilder.Create(), CancellationToken.None));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Does_not_retry_a_filtered_outcome()
    {
        var calls = 0;
        var inner = new FakeOutboundEndpoint(_ => { calls++; return new DeliveryResult(DeliveryOutcome.Filtered, "content"); });
        var endpoint = new RetryingOutboundEndpoint(inner, Retry(3));

        var result = await endpoint.SendAsync(MessageContextBuilder.Create(), CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Filtered, result.Outcome);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Does_a_single_attempt_on_replay()
    {
        var calls = 0;
        var inner = new FakeOutboundEndpoint(_ => { calls++; return new DeliveryResult(DeliveryOutcome.Failed, "down"); });
        var endpoint = new RetryingOutboundEndpoint(inner, Retry(5));
        var context = MessageContextBuilder.Create();
        context.MarkReplay();

        var result = await endpoint.SendAsync(context, CancellationToken.None);

        Assert.Equal(DeliveryOutcome.Failed, result.Outcome);
        Assert.Equal(1, calls);   // the ForwardWorker owns replay retry, so inline retry is suppressed
    }

    [Fact]
    public async Task Honors_cancellation_during_backoff()
    {
        var inner = new FakeOutboundEndpoint(_ => new DeliveryResult(DeliveryOutcome.Failed, "down"));
        // Long backoff so cancellation lands during the delay between attempts, not on a send.
        var endpoint = new RetryingOutboundEndpoint(inner, Retry(5, BackoffKind.Fixed, backoffSeconds: 30));
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => endpoint.SendAsync(MessageContextBuilder.Create(), cts.Token));
    }
}
