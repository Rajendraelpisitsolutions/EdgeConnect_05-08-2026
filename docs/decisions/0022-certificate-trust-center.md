# ADR-0022: Certificate Trust Center — operator-facing UX for OPC UA cert lifecycle

**Status:** Proposed (2026-05-30)
**Date:** 2026-05-30
**Framing:** OPC UA certificate friction generates a disproportionate share of industrial-integration support calls. Today the operator's only mechanism for trust decisions is dropping `.der` files into specific filesystem folders the way UA Expert and Prosys teach. EdgeConnect ships a unified UI surface — the Trust Center — that replaces the file-shuffling workflow with a discoverable, operator-language flow. This ADR promotes the previously-filed task #53 into a structured surface backed by P7.

## Context

Across the multi-protocol pilot work (PRs 1–7c-4 of the OPC UA Client series) the operator and the support engineers repeatedly hit certificate trust problems:

- Self-signed gateway certificate rejected by server → operator must locate the server's "rejected" folder, move the cert to "trusted," restart, retry
- Server certificate trust-chain mismatch → operator must figure out where to drop the server's CA cert
- Certificate expiring → no warning surfaces until session establishment fails
- Multiple OPC UA endpoints (per ADR-0014 SecurityPolicyUri per-endpoint negotiation) → each has its own trust state
- Cert mismatch errors from the OPC Foundation stack surface as `BadCertificateUntrusted` codes that mean nothing to an operator

The state lives in disk folders. The operator has no aggregated view, no UI for moving certs between trust states, no expiry surfacing, no diff against what the server presented at the last connect attempt.

ChatGPT and the operator both elevated this to Tier 1 in the 2026-05-30 review. Promotes task #53 from generic "cert-trust UX surfacing" to a named surface (Trust Center) with structured contract.

## Decision

The Trust Center surface conforms to the following six rules.

### Rule 1 — Single page, four-tab structure

The Trust Center is a single route under `/security/trust-center` with four tabs:

1. **Our certificates** — the gateway's own certificate(s) for each OPC UA role (client app instance, server app instance). Shows: subject, issuer, validity window, expiry countdown, thumbprint, "renew" affordance.
2. **Trusted peers** — certs the gateway has been told to trust (peer client certs for the OPC UA Server role; peer server certs for the OPC UA Client role). Shows: subject, issuer, validity, source ("manually added" / "moved from Rejected on YYYY-MM-DD"), per-source-or-sink usage.
3. **Rejected** — certs the gateway has seen and not trusted. Shows: subject, when first observed, which connection attempt observed it, "move to Trusted" action.
4. **Issuers (CAs)** — root + intermediate CA certs the gateway trusts. Shows: same as Trusted Peers but flagged as CA.

Each tab is a sortable list with search. The "Move to Trusted" action on a Rejected cert opens a confirmation modal that walks the four-question framework (per P7): *what is this cert, what server presented it, what changed since last connection, what action will happen if you trust it*.

### Rule 2 — Server discovery surfaces cert evidence pre-trust

When the OPC UA Client wizard's "Discover" step finds an endpoint, the wizard presents the server's certificate alongside the endpoint description. The operator can:

- See subject, issuer, validity, thumbprint
- See whether this cert (by thumbprint) is already Trusted, Rejected, or unknown
- One-click "Trust this cert" from the wizard, without leaving the flow

Today the wizard accepts an endpoint URL with no cert affordance — the operator finds out about cert trust at runtime when the connection fails. This rule moves the trust decision into the configuration moment where it belongs.

### Rule 3 — Expiry surfacing per P7

Every cert (Our + Trusted + CA) has a structured expiry status displayed via the ADR-0027 chip system:

- 🟢 Healthy: >60 days to expiry
- 🟡 Warning: 30–60 days
- 🟠 Approaching: 7–30 days
- 🔴 Critical: <7 days or expired

The Trust Center summarises the worst status across all certs. Routes whose source/sink uses a cert in Warning or worse surface the chip on their Route Health Surface (ADR-0027). The Flight Recorder (ADR-0021) emits a `CertificateNearingExpiry` event when a cert crosses a threshold.

### Rule 4 — Operator-language error mapping

When an OPC UA connection fails with a certificate-related status code, the runtime maps the code to operator-language with a structured remediation pointer:

| Stack status | Operator-facing surface |
|---|---|
| `BadCertificateUntrusted` | "Server's certificate is in the Rejected folder. [Move to Trusted →]" |
| `BadCertificateInvalid` | "Server's certificate signature failed to verify. The CA isn't in the Trusted Issuers list. [Add CA →]" |
| `BadCertificateTimeInvalid` | "Server's certificate is expired or not-yet-valid. Server time may also be wrong. [Open Trust Center →]" |
| `BadCertificateHostNameInvalid` | "Server's certificate doesn't match the endpoint hostname. Possible misconfiguration or impersonation." |
| `BadCertificateIssuerRevoked` | "Server's certificate issuer was revoked. Trust must be re-established with a new CA." |

The mapping is deterministic (table lookup); the remediation link is a deep-link into the Trust Center pre-filtered to the relevant cert.

### Rule 5 — Audit trail for trust changes

Every trust change (move to Trusted, remove from Trusted, add CA, revoke) writes an audit-trail entry with the operator identity, timestamp, cert thumbprint, and the previous + new trust state. Trust changes are governance-relevant — buyers in pharma / automotive will require this trail.

The Trust Center's "history" affordance surfaces the audit entries filtered to cert events.

### Rule 6 — No phone-home, no auto-trust

Per the architectural lock (CLAUDE.md §3 item 8 — Licenses are fully offline; the principle generalises), the Trust Center NEVER:

- Phones home to validate certs against a remote CA registry
- Auto-trusts a cert based on issuer "popularity"
- Pre-populates a Trusted Issuers list from an Anthropic-supplied or vendor-supplied bundle
- Calls out to the OS Windows certificate store for "system trusted" CAs

Trust is an explicit operator decision, always. The Trust Center surfaces the data needed to make that decision well.

## Consequences

**Positive:**

- Cert friction (the largest single category of OPC UA support calls) moves from "find the folder" to "click the action"
- Operators who never write a config file (the P6 audience) can complete cert workflows without engineer help
- Cert expiry stops being an after-the-fact surprise — Warning chips appear weeks ahead of the actual failure
- The four-question framework (P7) materialises: *what cert is this, why is connection failing, what changed (server presented a new cert), what action (trust / reject / contact server admin)*
- Composes with the Adapter Self-Test (per Phase C) — Self-Test for OPC UA Client surfaces cert state as one of the structured steps

**Negative:**

- Building a cert-management UI is non-trivial. Mitigation: scope Rule 1's four tabs as the MVP; Rules 2–6 follow as separate sub-tasks.
- Audit trail (Rule 5) integrates with the existing audit chain (already proven by config audit); needs a new entry-kind. Mechanical.
- Cert state lives in OPC Foundation stack-managed folders today. The Trust Center wraps that storage; if the stack changes its folder convention, the wrapper needs to update. Tractable.
- OPC UA Server role's certificate trust state is symmetric (server trusts client certs); the Trust Center must handle both directions. Cross-role state aggregation needs careful UI scoping.

**Forbidden patterns:**

- A modal popup at runtime "trust this cert?" that requires an operator to be watching at the moment of connection (cert decisions are a pre-flight workflow, not a runtime interrupt — this would violate P1's observational rule)
- A "trust everything from this CA" toggle without per-cert audit trail
- Hidden trust state — every trusted / rejected cert is visible in the surface; nothing trusted "implicitly"

## Reference

- Task #53 — original placeholder this ADR promotes to structured surface
- ADR-0014 — SecurityPolicyUri per-endpoint negotiation (the cert decision happens per-endpoint per-policy)
- ADR-0021 — Route Flight Recorder (emits `CertificateNearingExpiry` events)
- ADR-0027 — Route Health Surface (cert chip surfaces on routes that depend on the cert)
- Platform principle P3 — security is spec-first (this ADR is the spec for cert UX)
- Platform principle P6 — operational product, not developer tool (cert workflow is the canonical operator pain)
- Platform principle P7 — surfaces explain outcomes (Rule 4 is P7 applied to cert errors)
- `docs/sessions/2026-05-30-diagnostic-strategy-handoff.md`
