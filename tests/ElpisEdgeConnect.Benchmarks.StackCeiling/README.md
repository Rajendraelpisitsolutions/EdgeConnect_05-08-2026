# StackCeiling — OPC UA Foundation .NET stack measurement harness

**Status:** Skeleton (2026-05-29). Ready to run on the dedicated benchmark host once UA Sample Server is launched and the host's specs are captured in `phase2-multi-protocol-baseline.md` §7.

**Reference:**
- Plan v2.1 §6 Q9 + §2 (OPC UA Foundation stack audit) + §5.5 (benchmark validity rules)
- `docs/benchmarks/multi-protocol-workload-profiles.md` §3 Profile A/B/B′
- `docs/benchmarks/phase2-multi-protocol-baseline.md` §1, §2, §6
- kickoff handoff §3 Stream 1

> **Governance lock (v2.1 §6 Q10):** Only nightly dedicated-host numbers update locked baselines. PR-smoke shared-runner runs are informational only and never overwrite locked numbers.

---

## §1 Launch a UA Sample Server

The harness expects a working OPC UA endpoint to subscribe against. The OPC Foundation reference stack ships a sample server that's the closest thing to a known-good calibration target.

**Option A — OPC Foundation Reference Server (recommended for primary baseline)**

```bash
git clone https://github.com/OPCFoundation/UA-.NETStandard-Samples
cd UA-.NETStandard-Samples/Workshop/Reference
dotnet run -c Release -- /AutoAccept
```

Listens on `opc.tcp://localhost:62541` by default. The harness's `OPCUA_BENCHMARK_ENDPOINT` default matches this — no env-var needed.

**Option B — Real FactoryTalk / Kepware endpoint (week-6 calibration)**

```bash
export OPCUA_BENCHMARK_ENDPOINT='opc.tcp://factorytalk.pilot.local:4840'
dotnet run -c Release -- --filter '*StackCeiling*'
```

Real-endpoint runs verify the lab numbers track real-world (per `multi-protocol-workload-profiles.md` §6 open item 3).

---

## §2 Run the 4 phases

Locked sequence per v2.1 §6 Q9 (do NOT reorder):

```bash
dotnet run --project tests/ElpisEdgeConnect.Benchmarks.StackCeiling \
  --configuration Release \
  -- --filter '*'
```

Runs in order:
1. `Warmup_15K_5min` — 5-min warmup at 15K items
2. `Sustained_30K_30min` — 30-min sustained at 30K items (**PRIMARY TARGET**)
3. `Stretch_50K_30min` — 30-min stretch attempt
4. `Exploratory_75K_30min` — 30-min exploratory ceiling

Total wall time: ~95 minutes.

To run a single phase (e.g. just the 30K primary):

```bash
dotnet run --project tests/ElpisEdgeConnect.Benchmarks.StackCeiling \
  --configuration Release \
  -- --filter '*Sustained_30K_30min*'
```

---

## §3 What gets locked vs what stays informational

| Phase | Outcome → locked baseline doc destination |
|---|---|
| **Warmup_15K_5min** | Always informational — warmup numbers don't update §1–§5; logged for thermal/JIT diagnosis |
| **Sustained_30K_30min** with all 7 gates green | **Locks `phase2-multi-protocol-baseline.md` §1** as the primary baseline |
| **Sustained_30K_30min** with ANY gate failing | Goes to §6 "Informational — did not meet gate" |
| **Stretch_50K_30min** with all 7 gates green | **Locks §2** as the stretch baseline (this is the Profile B target) |
| **Stretch_50K_30min** with ANY gate failing | Goes to §6 with the failing gate documented |
| **Exploratory_75K_30min** | ALWAYS goes to §6 — exploratory ceiling is informational by design (never a customer-facing claim) |

---

## §4 7 sustainability gates (per workload-profiles.md §2)

A run that fails ANY of these does NOT count toward the locked number. Capture each in the per-phase entry in `phase2-multi-protocol-baseline.md`:

| Gate | Threshold | Current measurement source |
|---|---|---|
| Throughput drift (first 5-min mean vs last 5-min mean) | <5% across run window | Computed from per-minute notification-rate samples |
| p99 latency drift | <20% across run window | Per-monitored-item publish timestamp deltas |
| OPC UA notification queue depth | Bounded <500 at any sample point | **TODO** — reflection wrapper on `Subscription.m_messageCache` (v2.1 §2.6); week-1 host-side work |
| Buffer depth | Bounded, not monotonically growing | N/A for stack-ceiling (no buffer in scope) |
| Gen-2 GC frequency | <1 per 60s | `PhaseResult.Gen2CollectionsDuringRun / Elapsed.TotalSeconds` |
| Reconnect recovery | Within 15s ceiling | N/A for stack-ceiling (no induced reconnect noise) |
| `ReconfigureAsync` injection | Apply within 100ms, no data loss | N/A for stack-ceiling |

**Gates marked TODO need the host-side host-specific work before primary-baseline locking is meaningful.** That work happens in week-1 once the host is provisioned.

---

## §5 12 locked tuning knobs (v2.1 §2.5)

Codified as `const` in `LockedTuningKnobs.cs`. Any drift between the plan doc and the running benchmark is caught at compile time. Changing any of these invalidates the locked baseline.

See `LockedTuningKnobs.cs` for the full list with rationale.

---

## §6 Skeleton scope — what's NOT yet in this commit

Intentional carve-outs for week-1 host runs (kickoff §3 Stream 1 was scoped as "ready to point at UA Sample Server day-of-host-arrival", not "complete sustainability evaluation"):

1. **Industrial-COV workload generator** (workload-profiles §1 Rule 1) — currently uses Sample Server's natural `ServerStatus.CurrentTime` cadence. Real industrial-mix value-change distribution is week-1 work.
2. **Heterogeneous payload-mix verification** (§1 Rule 2) — currently subscribes uniform `DateTime` nodes. Mixed-type subscription is week-1 work.
3. **Notification queue depth reflection wrapper** (v2.1 §2.6) — currently a TODO; gate evaluation is incomplete without it. ~50 LOC of reflection on `Subscription.m_messageCache`.
4. **7-gate sustainability evaluation logic** — currently we emit raw throughput numbers. Gate evaluation + pass/fail decision is week-1 work.
5. **Reconnect noise injection** (workload-profiles §1 Rule 5) — applies to 24-hour soak runs, NOT stack-ceiling. Will land in a separate `Soak` benchmark project after stack-ceiling locks.

The skeleton's value is that it locks the 12 tuning knobs in compile-time-checked code AND establishes the 4-phase contract. Numbers measured today on a non-dedicated host are informational; numbers measured on the dedicated host in week 1 (after the TODOs above are filled in) update `phase2-multi-protocol-baseline.md`.
