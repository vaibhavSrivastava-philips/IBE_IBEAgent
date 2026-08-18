using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.TestKit;

public sealed class RecordingReplyContext : IReplyContext
{
    public MessageContext? Attached { get; private set; }
    public int? ArmedRequiredTotal { get; private set; }
    public bool WasFiltered { get; private set; }
    public string? FilterReason { get; private set; }
    public List<(bool Required, DeliveryResult Result)> Reports { get; } = [];
    
    public void Attach(MessageContext message) => Attached = message;
    public void OnFannedOut(int requiredTotal) => ArmedRequiredTotal = requiredTotal;
    public void ReportFiltered(string? reason = null) { WasFiltered = true; FilterReason = reason; }
    public void ReportLeg(int outputId, bool required, in DeliveryResult result) => Reports.Add((required, result));
}