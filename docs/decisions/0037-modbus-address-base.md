# 0037 — Modbus tag addresses accept an operator-declared address base

**Status:** Accepted (2026-07-20)
**Relates to:** `docs/modbus-address-base-design.md` (design note); PHASE3_EXECUTION_PLAN.md F4 (zero-based address contract); ADR-0015 (wizard contract); ADR-0020 (bundle redaction key tiers); ADR-0032 (Modbus RTU inside the Modbus module)

## Context

EdgeConnect is **zero-based end-to-end** for Modbus addresses, and has been since
F4: `ModbusTagDefinition.Address` is documented "zero-based register/coil
address", the scan planner computes per-tag offsets as `Address − blockStart`,
and the value is passed unchanged into FC01/02/03/04. Nothing in the stack
translates an address.

The field, however, does not speak zero-based. Vendor manuals, HMIs and most PLC
documentation print the **Modicon "4xxxx" data-model form**: holding register
`40033` is wire address `32`. Some tools use one-based. An operator reading the
PLC manual naturally types `40033`.

Enforcement of the zero-based contract existed in **exactly one** entry path:

- **CSV import** rejected legacy notation (10001–19999, 30001–49999) with a clear
  "subtract 40001" error. ✅
- **Studio wizard, hand-edited `current.json`, and the management API** accepted
  it silently — `40033` is a perfectly valid `ushort`. ❌

The silent path is the damaging one. A device almost never maps register 40033,
so the block read fails and `EmitBadPoint` emits `Value=null, Quality=Bad,
ValueType=Null` for **every tag in the block**. The operator sees what looks
exactly like a device or wiring fault, with no message anywhere pointing at the
notation mismatch. This was hit during a live customer investigation; notably the
customer's own Modbus→MQTT bridge script computes `40033 - 40001` by hand, which
is direct evidence that the conversion is a known, manual, error-prone step we
were pushing onto the operator.

Doing nothing was rejected: documentation cannot prevent a silent misread, and
the failure mode costs hours of misdirected field debugging.

## Decision

Add an **operator-declared address base** at the Modbus source configuration
surface, and normalise to zero-based **once, at the configuration edge**.

- **`ModbusAddressBase` enum**: `ZeroBased` (default) | `OneBased` | `Modicon`.
- **`addressBase` connection key** on the Modbus source `Connection` block
  (added to `ModbusTcpConnectionKeys.All`, so ADR-0020 redaction coverage is
  automatic — the key is benign).
- **Conversion happens once**, in `ModbusTcpSourceConfiguration.FromSourceInstance`,
  while reading `tagDefinitions`. Per register class under `Modicon`, the data-model
  prefix is subtracted: coils `1`, discrete inputs `10001`, input registers `30001`,
  holding registers `40001`. `OneBased` subtracts 1. An entered value that cannot
  produce a valid 0..65535 wire address is a hard config error naming the class's
  first legal address.
- **The internal and wire contract does NOT change.** The planner, executor,
  diagnostics and wire continue to see zero-based `ushort` addresses. The F4 lock
  is preserved; this is an *input-normalisation* layer, not an unlock.
- **`ModbusTagValidator` gains a silent-misconfiguration guard**: under
  `ZeroBased`, an address in the Modicon ranges is a **validation error** carrying
  the corrected address ("…enter the zero-based address 32"). Because the
  validator is shared, this closes the hole for *every* entry path — wizard,
  hand-edited JSON, API and import alike — turning a runtime null/Bad mystery into
  a config-apply-time error with the fix in the message.
- **Studio wizard** gains an "Address base" dropdown and a live **Wire addr**
  column per tag row, so the operator sees `40033 → 32` before applying.
- **CSV importer** honours the base (converting instead of rejecting) when one is
  supplied; under the default `ZeroBased` it keeps its existing rejection, now
  also mentioning the `addressBase` option.

### Supporting choices

1. **Explicit declaration, not auto-detection.** An "address ≥ 40001 ⇒ Modicon"
   heuristic was rejected: a device *may* legitimately map register 40033, and
   silently guessing the operator's intent is precisely the failure mode this ADR
   exists to remove. The heuristic survives only as the *validation warning* above,
   where it informs rather than decides.
2. **Source-level, not per-tag.** One device speaks one notation in practice.
   Per-tag notation would multiply failure modes for a rare case; it can be added
   later without breaking this design.
3. **Default `ZeroBased` ⇒ no migration.** Every configuration written before this
   change parses and behaves identically; the conversion is a no-op. No config
   rewrite, no version bump, no compatibility shim.
4. **Convert at the edge, not in the hot path.** Doing it in the planner or
   executor would spread notation awareness through the runtime and risk breaking
   the locked zero-based invariant. One conversion site keeps the blast radius at
   config parsing.
5. **Reuse `ModbusErrors.ImportLegacyAddress`** for the validator error so the
   importer and the validator report the same code for the same mistake.

## Consequences

- Operators may enter addresses **exactly as printed in the PLC manual** by
  declaring `"addressBase": "Modicon"`, or keep entering zero-based addresses with
  no config change at all.
- The previously silent misconfiguration now **fails at config-apply time** with
  the correct address in the message, instead of surfacing as `Quality=Bad /
  Value=null` that reads as a device fault.
- A pre-existing unit test (`BrowseTagsAsync_MapsTagDefinitionsToCanonicalTagDefs`)
  used `Address = 40001` under zero-based addressing and **began failing** when the
  guard landed. That is a true positive — the fixture itself contained the exact
  misconfiguration this ADR addresses — and it was corrected to a realistic
  zero-based address. Treat it as evidence the guard works on real code.
- This is an **additive** change conflicting with no locked decision: the
  protocol-agnostic core, canonical model, modular assemblies and licensing are
  untouched, and F4's zero-based internal contract is preserved rather than
  unlocked. No superseding ADR is required.
- Scope is Modbus only. Other protocols (S7, MELSEC) have their own addressing
  conventions and are explicitly out of scope here.
- **Not solved by this ADR:** an adjacent cause of all-null Modbus reads is
  *register batching across unmapped gaps* — with `maxGapRegisters > 0` the planner
  coalesces spaced tags into one block spanning gap registers, and a device that
  does not map those gaps rejects the **whole block** with Illegal Data Address
  (0x02). Verified against a sparse-register simulator. `maxGapRegisters: 0` means
  "tolerate no gap" and only splits tags separated by a positive gap — it mitigates
  *this* layout but does **not** force one-read-per-tag (contiguous/overlapping tags
  still coalesce at 0). Gap-aware block splitting is a separate follow-up.
- Follow-ups: update `docs/config-authoring.md` and the Modbus adapter guide with
  the new key and the notation table.
