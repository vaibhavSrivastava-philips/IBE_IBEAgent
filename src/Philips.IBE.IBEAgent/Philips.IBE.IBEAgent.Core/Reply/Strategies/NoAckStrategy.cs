using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// §6/§8 — no-ack (fire-and-forget) reply mode: the contract declares Acknowledgement.IsEnabled =
// false, so no bytes are ever written back to the source. RepliesOnReceipt = true so ReplyContext
// still "fires" (settling the one-shot state / disposing the timeout) at fan-out time instead of
// waiting on required legs that nobody will observe the outcome of.
public sealed class NoAckStrategy : IAckStrategy
{
    public bool RepliesOnReceipt => true;

    public Task WriteReplyAsync(MessageContext context, DeliveryResult result) => Task.CompletedTask;
}
