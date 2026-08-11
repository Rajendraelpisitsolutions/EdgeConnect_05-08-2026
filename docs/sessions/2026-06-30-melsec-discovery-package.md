# Mitsubishi MELSEC — Discovery Package

**Date:** 2026-06-30
**Status:** Active — answers feed the MELSEC plan v3 lock and ADR-0033.
**Why this exists:** [Plan v2](2026-06-30-melsec-source-adapter-plan-v2.md) sets the
Slice-1 runtime support to **MC 3E binary over TCP, read-only** and keeps every
other frame/transport mode config-accepted-but-validation-rejected. This package
is the data needed to (a) confirm the customer's hardware fits that supported
subset, or (b) tell us which currently-unimplemented mode must be promoted into
scope. Hand **Part A** to the site/controls engineer; **Part B** is the per-CPU
documentation we need before we trust a byte on the wire.

> **One key difference from a serial protocol:** MELSEC here is **Ethernet**, so
> there is no bus-master conflict, no RS-485 wiring, and no USB-adapter
> qualification. The risks are instead: wrong **frame mode / encoding**, **device
> read access not enabled** on the CPU, **port/route parameters**, and **word
> order** for multi-word values. Those are what the questions below pin down.

---

## Part A — Customer / site questionnaire

> One copy **per CPU / Ethernet interface** we will collect from.

### Q1 — PLC / CPU model (ASK FIRST — this decides whether Slice 1 fits)
- CPU model(s)? (e.g. iQ-R `R08`, iQ-F `FX5U`, `Q03UDV`, `L26CPU`, QnA, A-series): ______
- Single model or **mixed fleet**? How many units total? ______
- CPU/firmware version, if known: ______

> Slice 1 targets modern iQ-R / iQ-L / Q / L families (3E binary). **QnA and
> A-series are out of Slice-1 scope** — flag them here so we can scope separately
> rather than discover it on site.

### Q2 — Ethernet interface
- Built-in CPU Ethernet port, or a **separate Ethernet module**? Module model
  (e.g. `RJ71EN71`, `QJ71E71-100`, FX5 built-in): ______
- IP address(es) of the interface(s) we will connect to: ______
- Subnet / gateway / any NAT between the EdgeConnect box and the CPU: ______

### Q3 — Communication protocol setting on the module
These come from the module's "External Device Configuration" / open-method setup:
- Protocol: ☐ **MC Protocol 3E** ☐ MC 4E ☐ MC 1E ☐ SLMP ☐ other: ______
- Encoding: ☐ **Binary** ☐ ASCII
- Transport: ☐ **TCP** ☐ UDP
- **TCP port** (or UDP port) opened for the MC/SLMP connection: ______
- Is the connection **fixed/active-open by the device**, or **passive/MELSOFT/
  unpassive open** accepting an external client? (We are the client — we need a
  passive/unpassive-open or SLMP-server port we may connect into.): ______

> **Slice-1 supported = MC 3E + Binary + TCP.** Any other combination either drops
> out of scope or must be explicitly promoted (drives plan-v3 support matrix).

### Q4 — Route / frame parameters
For the 3E frame header we need these (defaults in brackets; confirm only if the
module is non-default):
- Network No. [0x00]: ______
- PC (station) No. [0xFF]: ______
- Request destination module I/O No. [0x03FF]: ______
- Request destination module station No. [0x00]: ______
- Are these a single local CPU, or **routed across a network** (CC-Link IE / MELSECNET)? ______

### Q5 — Read access enabled?
- Is **external device read access / "Communication Data Code" + online change**
  permitted on the module? ☐Y ☐N ☐unknown
- Any **IP allow-list / firewall** on the module or network restricting who may
  connect? ______
- Connection-count limit on the module (how many external clients can connect at
  once)? ______

### Q6 — Existing pollers (contention)
- Is any SCADA / HMI / historian / MES already connecting to this CPU's Ethernet
  interface (GX Works3, GOT, Kepware, etc.)? ☐Y ☐N — which: ______
- Will EdgeConnect connect **concurrently** with them, and is there a spare
  connection slot (see Q5 connection-count)? ______

### Q7 — Required data & scan rates (the tag list)
Per signal — this becomes the adapter tag list and drives the scan planner:

| Tag name | Device address | Data type | Word order (if 32/64-bit) | Unit | Required scan (ms) |
|----------|----------------|-----------|---------------------------|------|--------------------|
| | (e.g. `D100`) | (e.g. Int16) | n/a | | |
| | (e.g. `D200`) | Int32 / Float32 | low-word-first? high? | | |
| | (e.g. `W1A`) | UInt16 | n/a | | |
| | (e.g. `D100.3`) | Bool (word-bit) | n/a | | |

- Approx. **total tag count** and **fastest** required scan interval: ______
- Any **word-bit** points needed (a bit inside a word register, e.g. `D100.3`)? ______

> Note device-letter radix conventions we will apply: `X Y B W SB SW ZR` are read
> as **hexadecimal** addresses; `D M L F V S T C R SM SD Z` as **decimal**.
> Please write addresses exactly as they appear in GX Works3.

### Q8 — Word order for multi-word values
For any 32-bit (DINT/REAL) or 64-bit value: are they stored **low-word-first**
(default) or **high-word-first**? If unknown, we will confirm from a known-good
capture (Part B) rather than guess. ______

### Q9 — Read vs write
- Is collection **read-only** acceptable for v1? ☐Confirmed
- If setpoint **writes** are needed soon, say so — writes are a separate scope
  (gated behind explicit enable + per-tag opt-in + validation + audit) and change
  the delivery timeline.

### Q10 — Edge box placement
- Where does the EdgeConnect box sit relative to the CPU (same VLAN / subnet /
  routed)? ______
- OS: ☐Windows ☐Linux (distro: ____) ______

---

## Part B — Per-CPU documentation checklist

For each CPU we need the **exact manual revision** for its MC/SLMP frame **and a
known-good request/response capture**, so we can pin device-code bytes and word
order with golden-byte tests. A vendor statement of "supports MC Protocol" is not
a frame spec.

- [ ] Confirm the controlling reference is **SH(NA)-080008** (SLMP/MC reference)
      or the module-specific MC manual, and note its revision.
- [ ] GX Works3 / GX Works2 **device list** for the required tags (so addresses
      and types are authoritative, not transcribed).
- [ ] Module "External Device Configuration" export (protocol, encoding, transport,
      port, open method, route params) — backs Q3/Q4.
- [ ] **One known-good capture** (Wireshark on the link, or a vendor tool trace) of
      a successful batch-read request **and** response against this CPU — this
      verifies: subheader (`5000`/`D000`), the device-code byte for each device we
      read, low-byte-first field encoding, and **word order** for one 32-bit value.
- [ ] If any non-3E-binary-TCP mode is in use (Q3), flag it explicitly — it gates
      whether that mode must be promoted into scope or the module reconfigured.

---

## Outputs that feed plan v3 + ADR-0033

1. Q1/Q3 → confirm the fleet fits **Slice-1 (MC 3E binary / TCP)**, or list which
   mode/family must be promoted to its own scoped slice.
2. Q4 → the route-field defaults vs overrides the config must carry.
3. Q5/Q6 → connection-access + contention reality (passive-open port available,
   spare connection slot).
4. Q7/Q8 + Part B capture → the real tag profiles, **word-order presets**, and the
   golden-byte fixtures for the codec tests.
5. Q9 → confirms the **read-only** Slice-1 boundary (or triggers a write-scope
   conversation).
6. Part B manual revision + capture → device-code byte table pinned against
   SH-080008 before any wire code is written.

---

## Appendix FX5 — iQ-F / FX5 capture guidance (added 2026-07-03, A-2 Gate A-2S)

> Purpose: field-qualification capture guidance for FX5-family CPUs (e.g. the
> customer's FX5U-32MT) over the CPU's **built-in Ethernet** port. Authoritative
> manual: **SH(NA)-082625ENG-J** (FX5 User's Manual (Communication), April 2026);
> see `docs/sessions/2026-07-03-melsec-a2d-fx5-audit.md` for the pinned bundle.
> This appendix is documentation only — hardware capture remains pending.

> Studio note (A-2O): in EdgeConnect's MELSEC wizard, select the **iQ-F / FX5**
> PLC family profile in the "PLC connection" section before entering tags — it
> applies the FX5 device set (no ZR) and octal X/Y address notation.

### Where the SLMP settings live (GX Works3)

1. Navigate: **Parameter → FX5UCPU → Module Parameter → Ethernet Port**.
2. **Basic Settings → Own Node Settings** — record the CPU IP address.
3. **Basic Settings → External Device Configuration** — add / locate the
   **SLMP Connection Module** entry. Record:
   - **Port No.** (operator-chosen; there is no universal default),
   - protocol: **TCP** (EdgeConnect Slice 1 is TCP-only; UDP is out of scope),
   - the number of configured connections (contention check — other SCADA/HMI
     pollers may hold SLMP connections too).
4. If "Set Processing Counts" was changed from default, note it (it shapes
   service capacity per scan; SH-082625ENG-J SLMP chapter).

### Suggested sample tags for the capture (FX5)

| Address | Note |
|---|---|
| `D100` (any project-known D) | word device, decimal — primary path |
| `D<n>` holding a known 32-bit value | pins **word order** (LowWordFirst expected) |
| `W1A` or any `W` | hex-radix word device |
| `R100` | file register (follows the CPU's File Register Setting) |
| `M100` | bit device via word read |
| `X10` / `Y10` | **octal operator label** (X10 = the 9th input); pins the octal→numeric wire rule (binary head is numeric per SH-082625ENG-J §38.2) |
| `SD210` or another documented `SD` | special register (A-3a); confirms special-device access on the CPU |
| `TN0` / `CN0` (a documented timer/counter) | timer/counter **current value** (A-3b); explicit mnemonics only — bare `T0`/`C0` are rejected |
| — | Note: **ZR is NOT accessible on FX5 CPUs** — do not include ZR tags |

### Capture checklist (Part B, FX5 flavor)

1. Wireshark/tcpdump on the EdgeConnect host filtered to the SLMP port
   (`tcp.port == <port>`), capturing BOTH directions.
2. Read each sample tag once via the Studio **Test read** (or any SLMP client);
   simultaneously record the PLC-side values from GX Works3 watch.
3. Deliverables per read: request bytes, response bytes, GX Works3-displayed
   value, tag address as typed by the operator.
4. Induce ONE error case for end-code evidence: read a deliberately out-of-range
   device number and capture the returned end code.
5. Return: the pcap, the value sheet, CPU model/firmware, GX Works3 parameter
   screenshots (Ethernet Port + External Device Configuration), and the manual
   revision shipped with the project if available.

Verification targets fed by this capture: golden bytes, word order, X/Y
octal→numeric rule, bit-device alignment, FX5 end codes → flips the iQ-F profile
to **Field-qualified** (roadmap §5).
