using System.Text;
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
        Assert.Equal(DeliveryOutcome.Accepted, strategy.LastOutcome.Outcome);
    }

    [Fact]
    public void Delivery_strategy_waits_for_all_required_legs()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);
        reply.Attach(MessageContextBuilder.Create());

        reply.OnFannedOut(2);
        Assert.Equal(0, strategy.Calls);   // nothing yet

        reply.ReportLeg(10, true, new DeliveryResult(DeliveryOutcome.Delivered));
        Assert.Equal(0, strategy.Calls);   // 1 of 2

        reply.ReportLeg(20, true, new DeliveryResult(DeliveryOutcome.Delivered));
        Assert.Equal(1, strategy.Calls);   // 2 of 2 -> fire
        Assert.Equal(DeliveryOutcome.Delivered, strategy.LastOutcome.Outcome);
        Assert.Equal(2, strategy.LastOutcome.LegResults.Count);
    }

    [Fact]
    public void Delivered_orders_leg_results_by_output_id()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);
        reply.Attach(MessageContextBuilder.Create());

        reply.OnFannedOut(2);
        reply.ReportLeg(20, true, new DeliveryResult(DeliveryOutcome.Delivered, ResponsePayload: Encoding.UTF8.GetBytes("B")));
        reply.ReportLeg(10, true, new DeliveryResult(DeliveryOutcome.Delivered, ResponsePayload: Encoding.UTF8.GetBytes("A")));

        var legs = strategy.LastOutcome.LegResults;
        Assert.Equal("A", Encoding.UTF8.GetString(legs[0].ResponsePayload.Span));   // OutputId 10 sorts first
        Assert.Equal("B", Encoding.UTF8.GetString(legs[1].ResponsePayload.Span));
    }

    [Fact]
    public void Required_failure_fires_nack()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);
        reply.Attach(MessageContextBuilder.Create());

        reply.OnFannedOut(1);
        reply.ReportLeg(10, true, new DeliveryResult(DeliveryOutcome.Failed, "downstream"));

        Assert.Equal(1, strategy.Calls);
        Assert.Equal(DeliveryOutcome.Failed, strategy.LastOutcome.Outcome);
        Assert.Equal("downstream", strategy.LastOutcome.Reason);
    }

    [Fact]
    public void Optional_legs_never_fire_the_reply()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);
        reply.Attach(MessageContextBuilder.Create());

        reply.OnFannedOut(1);   // one REQUIRED leg outstanding (never reports)
        reply.ReportLeg(10, false, new DeliveryResult(DeliveryOutcome.Delivered));
        reply.ReportLeg(20, false, new DeliveryResult(DeliveryOutcome.Failed, "x"));

        Assert.Equal(0, strategy.Calls);   // optional legs don't gate; still waiting on the required leg
    }

    [Fact]
    public void Zero_required_legs_settle_immediately_under_a_delivery_strategy()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);   // infinite timeout: settle cannot come from a timeout
        var disposition = new RecordingDisposition();
        reply.Attach(new MessageContext("c", 1, MessageFormats.Hl7v2, new FakeAckToken(), new RecordingReplyContext(), disposition: disposition));

        reply.OnFannedOut(0);   // nothing required to await -> settle now, not at the ack timeout

        Assert.Equal(1, strategy.Calls);
        Assert.Equal(DeliveryOutcome.Delivered, strategy.LastOutcome.Outcome);   // vacuously delivered
        Assert.Equal(1, disposition.Completions);                                // source disposes (e.g. a File moves to processed/)
        Assert.Equal(MessageCompletion.Completed, disposition.LastOutcome);
    }

    [Fact]
    public void ReportFiltered_fires_once_with_filtered()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);
        reply.Attach(MessageContextBuilder.Create());

        reply.ReportFiltered();

        Assert.Equal(1, strategy.Calls);
        Assert.Equal(DeliveryOutcome.Filtered, strategy.LastOutcome.Outcome);
    }

    [Fact]
    public void ReportFiltered_passes_reason_to_the_strategy()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);
        reply.Attach(MessageContextBuilder.Create());

        reply.ReportFiltered("duplicate");

        Assert.Equal(1, strategy.Calls);
        Assert.Equal(DeliveryOutcome.Filtered, strategy.LastOutcome.Outcome);
        Assert.Equal("duplicate", strategy.LastOutcome.Reason);   // reason flows to the formatter
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
            .Select(_ => Task.Run(() => reply.ReportLeg(10, true, new DeliveryResult(DeliveryOutcome.Delivered))));
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

        Assert.Equal(DeliveryOutcome.Failed, strategy.LastOutcome.Outcome);
        Assert.Equal("reply timeout", strategy.LastOutcome.Reason);
    }

    [Fact]
    public void Disposition_completes_at_settle_with_delivered()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan);
        var disposition = new RecordingDisposition();
        reply.Attach(new MessageContext("c", 1, MessageFormats.Hl7v2, new FakeAckToken(), new RecordingReplyContext(), disposition: disposition));

        reply.OnFannedOut(1);
        reply.ReportLeg(10, true, new DeliveryResult(DeliveryOutcome.Delivered));

        Assert.Equal(1, disposition.Completions);
        Assert.Equal(MessageCompletion.Completed, disposition.LastOutcome);
    }

    [Fact]
    public void Disposition_completes_filtered_even_when_the_reply_is_suppressed()
    {
        var strategy = new StubStrategy(repliesOnReceipt: false);
        using var reply = new ReplyContext(strategy, Timeout.InfiniteTimeSpan, replyOnFilter: false);
        var disposition = new RecordingDisposition();
        reply.Attach(new MessageContext("c", 1, MessageFormats.Hl7v2, new FakeAckToken(), new RecordingReplyContext(), disposition: disposition));

        reply.ReportFiltered("dup");   // suppressed: no reply written...

        Assert.Equal(0, strategy.Calls);                       // ...but the source is still disposed
        Assert.Equal(1, disposition.Completions);
        Assert.Equal(MessageCompletion.Filtered, disposition.LastOutcome);
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

    private sealed class RecordingDisposition : IMessageDisposition
    {
        private int _completions;
        public int Completions => Volatile.Read(ref _completions);
        public MessageCompletion LastOutcome { get; private set; }
        public ValueTask CompleteAsync(MessageCompletion outcome, CancellationToken cancellationToken)
        {
            LastOutcome = outcome;
            Interlocked.Increment(ref _completions);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubStrategy(bool repliesOnReceipt) : IAckStrategy
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public ReplyOutcome LastOutcome { get; private set; }
        public bool RepliesOnReceipt => repliesOnReceipt;

        public Task WriteReplyAsync(MessageContext context, ReplyOutcome outcome)
        {
            LastOutcome = outcome;
            Interlocked.Increment(ref _calls);
            return Task.CompletedTask;
        }
    }
}
