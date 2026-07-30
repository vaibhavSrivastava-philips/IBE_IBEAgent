using Philips.IBE.IBEAgent.Abstractions;
using Philips.IBE.IBEAgent.Core;
using Philips.IBE.IBEAgent.TestKit;

namespace Philips.IBE.IBEAgent.Core.UnitTests;

public sealed class ReplyContextTests
{
    [Fact]
    public void OnFannedOut_with_receipt_strategy_fires_once_with_accepted()
    {
        var strategy = new StubStrategy(repliesOnReceipt: true);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);
        reply.Attach(MessageContextBuilder.Create());

        reply.OnFannedOut(1);

        Assert.Equal(1, strategy.Calls);
        Assert.Equal(DeliveryOutcome.Accepted, strategy.LastResult.Outcome);
    }

    [Fact]
    public void Delivery_strategy_waits_for_all_required_legs()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);
        reply.Attach(MessageContextBuilder.Create());

        reply.OnFannedOut(2);
        Assert.Equal(0, strategy.Calls);   // nothing yet

        reply.ReportLeg(true, new DeliveryResult(DeliveryOutcome.Delivered));
        Assert.Equal(0, strategy.Calls);   // 1 of 2

        reply.ReportLeg(true, new DeliveryResult(DeliveryOutcome.Delivered));
        Assert.Equal(1, strategy.Calls);   // 2 of 2 -> fire
        Assert.Equal(DeliveryOutcome.Delivered, strategy.LastResult.Outcome);
    }

    [Fact]
    public void Required_failure_fires_nack()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);
        reply.Attach(MessageContextBuilder.Create());

        reply.OnFannedOut(1);
        reply.ReportLeg(true, new DeliveryResult(DeliveryOutcome.Failed, "downstream"));

        Assert.Equal(1, strategy.Calls);
        Assert.Equal(DeliveryOutcome.Failed, strategy.LastResult.Outcome);
        Assert.Equal("downstream", strategy.LastResult.Error);
    }

    [Fact]
    public void Optional_legs_never_fire_the_reply()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);
        reply.Attach(MessageContextBuilder.Create());

        reply.OnFannedOut(0);
        reply.ReportLeg(false, new DeliveryResult(DeliveryOutcome.Delivered));
        reply.ReportLeg(false, new DeliveryResult(DeliveryOutcome.Failed, "x"));

        Assert.Equal(0, strategy.Calls);
    }

    [Fact]
    public void ReportFiltered_fires_once_with_filtered()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);
        reply.Attach(MessageContextBuilder.Create());

        reply.ReportFiltered();

        Assert.Equal(1, strategy.Calls);
        Assert.Equal(DeliveryOutcome.Filtered, strategy.LastResult.Outcome);
    }

    [Fact]
    public void ReportFiltered_passes_reason_to_the_strategy()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);
        reply.Attach(MessageContextBuilder.Create());

        reply.ReportFiltered("duplicate");

        Assert.Equal(1, strategy.Calls);
        Assert.Equal(DeliveryOutcome.Filtered, strategy.LastResult.Outcome);
        Assert.Equal("duplicate", strategy.LastResult.Error);   // reason flows to the formatter
    }

    [Fact]
    public void ReplyOnFilter_false_suppresses_the_filtered_reply()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan, replyOnFilter: false);
        reply.Attach(MessageContextBuilder.Create());

        reply.ReportFiltered("duplicate");

        Assert.Equal(0, strategy.Calls);   // legacy silent drop: no reply written
    }

    [Fact]
    public async Task ReplyOnFilter_false_also_cancels_the_pending_timeout()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, TimeSpan.FromMilliseconds(50), replyOnFilter: false);
        reply.Attach(MessageContextBuilder.Create());

        reply.ReportFiltered("duplicate");   // suppress -> also disposes the timer
        await Task.Delay(200);                // wait past the would-be timeout

        Assert.Equal(0, strategy.Calls);      // no timeout NACK either
    }

    [Fact]
    public void Not_attached_does_not_fire_and_does_not_throw()
    {
        var strategy = new StubStrategy(repliesOnReceipt: true);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);

        reply.OnFannedOut(1);   // no Attach -> no message -> cannot write

        Assert.Equal(0, strategy.Calls);
    }

    [Fact]
    public async Task Fires_exactly_once_under_concurrent_reports()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);
        reply.Attach(MessageContextBuilder.Create());
        reply.OnFannedOut(1);

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => reply.ReportLeg(true, new DeliveryResult(DeliveryOutcome.Delivered))));
        await Task.WhenAll(tasks);

        Assert.Equal(1, strategy.Calls);
    }

    [Fact]
    public async Task Timeout_fires_failure_reply()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, TimeSpan.FromMilliseconds(50));
        reply.Attach(MessageContextBuilder.Create());

        await WaitForAsync(() => strategy.Calls == 1, TimeSpan.FromSeconds(2));

        Assert.Equal(DeliveryOutcome.Failed, strategy.LastResult.Outcome);
        Assert.Equal("reply timeout", strategy.LastResult.Error);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met in time");
            await Task.Delay(10);
        }
    }

    private sealed class StubStrategy(bool repliesOnReceipt) : IAckStrategy
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public DeliveryResult LastResult { get; private set; }
        public bool RepliesOnReceipt => repliesOnReceipt;

        public Task WriteReplyAsync(MessageContext context, DeliveryResult result)
        {
            LastResult = result;
            Interlocked.Increment(ref _calls);
            return Task.CompletedTask;
        }
    }
}
