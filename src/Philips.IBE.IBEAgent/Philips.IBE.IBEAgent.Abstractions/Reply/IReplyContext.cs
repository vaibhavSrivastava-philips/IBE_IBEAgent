namespace Philips.IBE.IBEAgent.Abstractions;

// A2 resolution: the reply-authority seam. Concrete ReplyContext lives in Core (§10) and implements this.
// Method surface reconciled from §12/§13 (A4): OnFannedOut == Arm; "received" is fired internally on receipt.
public interface IReplyContext
{
    void Attach(MessageContext message);
    void OnFannedOut(int requiredTotal);          // arm per-message with the applicable required-leg count
    void ReportFiltered();                        // shared-pipeline short-circuit -> reply "filtered"
    void ReportLeg(bool required, in DeliveryResult result); // a leg's terminal outcome
}