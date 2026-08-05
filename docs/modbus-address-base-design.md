# Modbus Address Base — Problem Statement & Proposed Fix

**Status:** Design proposal (not implemented). Needs owner sign-off + an ADR before build.
**Author:** Development Team · **Date:** 2026-07-18
**Applies to:** `ElpisEdgeConnect.Sources.ModbusTcp` (Modbus TCP + RTU sources), Studio Modbus wizard, CSV tag importer.

---

## 1. The question

> *Are EdgeConnect tag addresses zero-based end to end? The operator does not know whether they will
> use zero-based or one-based addressing. Based on configuration it should accept either and read the
> correct value.*

**Answer: yes — EdgeConnect is zero-based end-to-end today, and it is a locked contract.** There is
**no** option to enter Modicon/one-based addresses. That is the defect described below.

---

## 2. Current behaviour (verified in code)

The address the operator types is written **straight to the wire** with no translation:

| Layer | Contract | Reference |
|---|---|---|
| Config record | `Address` documented *"Zero-based register/coil address"*, typed `ushort` | `ModbusTcpSourceConfiguration.cs:398` |
| Scan planner | `StartAddress` *"Zero-based start address of the block"*; per-tag `Offset = Address - blockStart` | `Scanning/ScanPlan.cs:76`, `Scanning/ScanPlanner.cs:170` |
| Wire | Block start address passed unchanged into FC01/02/03/04 | `ModbusTcpSourceAdapter.PollBlockAsync` → `block.ToRequest(...)` |
| Studio wizard | `Address` is a plain `int`, cast `(ushort)` — **no base handling, no range guard** | `Wizards/ModbusSourceWizardModel.cs:263, 600` |
| CSV import | **Rejects** legacy notation (10001–19999, 30001–49999) with a helpful "subtract 40001" error | `Import/ModbusTagCsvImporter.cs:403-419` |
| Typed validator | **No address rules at all** | `ModbusTagValidator.cs` |

### 2.1 The hole

Enforcement exists in **exactly one** entry path:

- **CSV import** → Modicon notation is caught and explained. ✅
- **Studio wizard / manual entry / hand-edited `current.json`** → `40033` is a perfectly valid `ushort`,
  so it is **silently accepted** and read as register **40033** instead of **32**. ❌

### 2.2 Operator-visible symptom

A device almost never maps register 40033. The read fails, and per `EmitBadPoint`
(`ModbusTcpSourceAdapter.cs:603`) every tag in the block is emitted as:

```
value: null   quality: Bad   type: Null
```

There is no message telling the operator *"your address looks like Modicon notation."* This is a
**silent misconfiguration that presents as a device fault** — the operator blames wiring/PLC while the
cause is a notation mismatch.

### 2.3 Why this is easy to hit

The two conventions in the field are:

| Notation | Example (holding reg) | Wire address |
|---|---|---|
| **Zero-based logical** (what EdgeConnect requires) | `32` | 32 |
| **Modicon / data-model "4xxxx"** (what most PLC docs, HMIs and vendor manuals print) | `40033` | 32 |
| **One-based** (some vendors/tools) | `33` | 32 |

Vendor documentation overwhelmingly prints the **4xxxx** form, so an operator reading a PLC manual will
naturally type `40033`. Reference: the customer's own PLC bridge script computes `40033 - 40001` by
hand — proof that the conversion is a known, manual, error-prone step.

---

## 3. Proposed fix

**Principle: normalise at the edge; keep the core zero-based.** The internal/wire contract does *not*
change (the F4 lock is preserved). We add an explicit input-notation declaration at the configuration
surface and convert once, at config-parse time.

### 3.1 Config surface

Add to the Modbus source `Connection` block:

```jsonc
"addressBase": "ZeroBased"   // default — current behaviour, fully backward compatible
// "OneBased"                // subtract 1
// "Modicon"                 // subtract the class offset (4xxxx/3xxxx/1xxxx/0xxxx)
```

Conversion applied per register class when `Modicon`:

| Class | Operator enters | Subtract |
|---|---|---|
| Coil (0xxxx) | `00001`/`1` | 1 |
| Discrete input (1xxxx) | `10001` | 10001 |
| Input register (3xxxx) | `30001` | 30001 |
| Holding register (4xxxx) | `40001` | 40001 |

### 3.2 Where the conversion happens

**Once**, in `ModbusTcpSourceConfiguration.FromSourceInstance(...)` while reading `tagDefinitions`.
Everything downstream (planner, executor, diagnostics, wire) continues to see a zero-based `ushort`.
This keeps the locked contract intact and means **no change** to the hot path.

### 3.3 Close the silent-failure hole

Move the CSV importer's guard into **`ModbusTagValidator`** so it applies to *every* entry path
(wizard, hand-edited JSON, API, import):

- When `addressBase = ZeroBased` **and** an address falls in 10001–19999 or 30001–49999 →
  **validation error** (not a silent bad read):
  > *"Address 40033 looks like Modicon notation. Either set `addressBase: "Modicon"` or enter the
  > zero-based address 32."*

This converts today's runtime null/Bad mystery into a **config-apply-time error with the fix in the
message** — consistent with the draft → validate → apply contract.

### 3.4 Studio UX

On the Modbus source wizard:
- An **"Address base"** dropdown (Zero-based / One-based / Modicon 4xxxx), defaulted to Zero-based.
- A **live preview** next to the address field: `40033 → wire address 32`, so the operator sees the
  resolved value before applying.
- Inline warning if the typed value looks like Modicon while base is Zero-based.

### 3.5 CSV importer

Honour `addressBase` instead of hard-rejecting, keeping the current rejection as the behaviour when
the base is `ZeroBased` (unchanged default).

---

## 4. Alternatives considered

| Option | Verdict |
|---|---|
| **A. Explicit `addressBase` (recommended)** | Unambiguous, backward compatible, self-documenting in config, testable. |
| **B. Auto-detect** (`>= 40001` ⇒ Modicon) | Rejected: magical and unsafe — a device *may* legitimately map register 40033. Silent guessing is how the current bug feels. Acceptable only as a *warning*, which §3.3 already provides. |
| **C. Per-tag notation column** | Rejected for v1: per-tag mixing is rare and multiplies the failure modes; source-level is the real-world unit. Can be added later without breaking A. |
| **D. Do nothing, document harder** | Rejected: the wizard silently accepts bad input; documentation does not prevent a silent misread. |

---

## 5. Backward compatibility

- Default `addressBase = "ZeroBased"` ⇒ **every existing config behaves exactly as today**.
- No change to the wire protocol, scan planner, or canonical model.
- New validation only *adds* an error for input that is already broken at runtime today.

---

## 6. Implementation checklist

1. `ModbusEncapsulation`-style enum `ModbusAddressBase { ZeroBased, OneBased, Modicon }`.
2. `ModbusTcpSourceConfiguration`: add `AddressBase`; parse key in `FromSourceInstance`; apply the
   per-class offset when building `ModbusTagDefinition.Address`.
3. `ModbusTcpConnectionKeys`: add `addressBase` (required — the drift guard asserts key coverage).
4. `ModbusTagValidator`: add the Modicon-looking-address rule (§3.3).
5. `ModbusSourceWizardModel` + `AddModbusSource.razor`: dropdown, preview, inline warning.
6. `ModbusTagCsvImporter`: honour the base; keep current message for `ZeroBased`.
7. Tests: per-class conversion, default = today's behaviour, validator rejects Modicon under
   `ZeroBased`, round-trip through `FromSourceInstance`.
8. Docs: update `docs/config-authoring.md` + the Modbus adapter guide; add an ADR recording the
   decision.

---

## 7. Governance note

The "addresses are zero-based logical" F4 contract is **locked**. This proposal does **not** unlock it —
the internal and wire contract stay zero-based. It adds an *input-normalisation* layer at the config
edge. Because it changes the configuration contract, it should be recorded as an ADR
(`docs/decisions/00NN-modbus-address-base.md`) once approved.

---

## 8. Related

- Silent-failure symptom & diagnosis path: per-source log at
  `%ProgramData%\EdgeConnect\logs\<source>.txt` (DEVICE/CODE/DATA lines with probable-cause hints).
- Adjacent issue: **register batching across unmapped gaps.** With `maxGapRegisters > 0`, tags spaced
  apart are coalesced into one block spanning the gaps; a device that does not map those gap registers
  rejects the **whole block** with Illegal Data Address (0x02) and every tag reads null/Bad. Verified
  against a sparse-register simulator: a batched read of 0–40 returned `IllegalAddress`, while
  individual reads succeeded. Partial mitigation: `maxGapRegisters: 0` means "tolerate **no** gap", so
  it splits tags that have a positive gap between them into separate reads — which happens to isolate
  *this* layout (tags on a stride of 4), but it does **not** force one-read-per-tag in general
  (contiguous or overlapping tags have gap 0 and still coalesce). There is no setting that guarantees
  one transaction per tag; gap-aware / gap-splitting block planning is the real follow-up.
