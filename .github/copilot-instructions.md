# Copilot Instructions

## Project Guidelines
- User prefers SSL/TLS mutual vs one-way behavior to be inferred from configured certificate material (PFX/client cert/trusted CA/client-certificate requirement) rather than requiring explicit one-way/two-way mode selection, and ACK/no-ACK to remain independent from transport mode.
- User prefers clear SSL mode names such as None/Single/Mutual/Plain instead of OneWay/TwoWay because current naming is misleading. User wants SSL naming migration to be strict: remove legacy aliases and keep only standard terms (Plain, OneWay, Mutual) without backward-compatibility enum aliases.
- User prefers certificate configuration to support OS certificate stores/managers on both Windows and Linux instead of requiring PFX/cert password fields directly in endpoint configuration.
- For IBE Agent gap-closure work, exclude WebAgent, license management, and DB-backed store-and-forward unless explicitly requested.
- User wants final production-ready implementations and does not want dummy/mock placeholder implementations in core architecture paths like store-and-forward.
- When common behavior exists across protocols/transports, avoid protocol-specific duplicate files; refactor to generic shared abstractions and remove redundant protocol-specific helpers.
- User wants code changes to favor SOLID principles, generic/shared abstractions, and production-ready implementations rather than protocol-specific duplication or placeholder code.
- User prefers shared/generic implementations over protocol-specific duplicated helpers when the behavior is common across transports.
- User expects all protocol implementations (TCP/HTTP/WebSocket) to have standardized logging, proper exception handling, connection retry, and cleaner SOLID/static-analysis-aligned structure.
- User prefers TCP inbound/outbound code to follow SOLID/static analysis standards and expects proper exception logging plus connection retry behavior.

## Logging and Telemetry
- User wants production logging/telemetry to be low-noise by default, with developer/debug configuration able to enable detailed diagnostics when needed.
