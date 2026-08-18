using Philips.IBE.IBEAgent.Abstractions;

namespace Philips.IBE.IBEAgent.Core;

// §6/§8 — the resolved per-contract reply behavior handed to a ReplyContext: which strategy writes the
// reply, the reply timeout (finite for Enhanced ack), and whether a filtered/short-circuited message
// gets a reply at all (ReplyOnFilter=false reproduces the legacy "silent drop"). A named policy rather
// than a positional tuple so new reply knobs stay additive.
public sealed record ReplyPolicy(IAckStrategy Strategy, TimeSpan Timeout, bool ReplyOnFilter);
