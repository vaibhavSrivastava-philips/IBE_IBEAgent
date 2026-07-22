namespace Philips.IBE.IBEAgent.Abstractions;

// per-queue overflow policy. DropOldest/Newest intentionally absent (clinical data).
public enum OverflowPolicy { Wait, Reject, SpillToDisk }