namespace Philips.IBE.IBEAgent.Abstractions;

// Source-facing settle outcome for IMessageDisposition (deliberately coarser than the leg-level
// DeliveryOutcome/ReplyOutcome): Completed = message handled, Faulted = required delivery failed,
// Filtered = dropped in the pipeline.
public enum MessageCompletion { Completed, Faulted, Filtered }
