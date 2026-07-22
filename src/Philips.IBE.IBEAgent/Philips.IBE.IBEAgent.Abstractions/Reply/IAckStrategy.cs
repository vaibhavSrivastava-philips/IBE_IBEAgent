namespace Philips.IBE.IBEAgent.Abstractions;

// WHEN/WHAT: Normal (on receipt) | Enhanced (after delivery) | Response (request-reply).
public interface IAckStrategy
{
    bool RepliesOnReceipt { get; }         // true => Normal ack fires "received" at OnFannedOut
    Task WriteReplyAsync(MessageContext context, DeliveryResult result);
}