namespace Philips.IBE.IBEAgent.Configuration;

// §3.9 — which host owns the always-on ForwardWorker retry loop. Exactly one owner is active;
// the compiler rejects Ordered legs paired with the out-of-process owner (per-key order).
public enum ForwardOwner { InProcess, ForwardService }
