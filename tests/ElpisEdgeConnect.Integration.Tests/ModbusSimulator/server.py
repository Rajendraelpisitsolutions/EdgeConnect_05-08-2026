"""
Deterministic-plus-random-walk Modbus TCP simulator used by the Phase 3
integration tests AND by the Modbus-MQTT soak runs.

Uses pymodbus 3.x's synchronous TCP server. Register/coil values are seeded
to known patterns at startup so first-poll assertions are stable, then a
background thread nudges numeric values every second so a trend-aware
subscriber sees fresh data across multiple polls.

Default endpoint: 0.0.0.0:5020 (override with MODBUS_PORT env var).

=== S7-1200 flavouring (env-var configurable) =================================

Real-PLC quirks that the soak run benefits from approximating:

  MODBUS_SIM_JITTER_MS         (default 5)   — uniform 0..N ms delay per read.
                                                Models S7-1200 scan-cycle coupling.
  MODBUS_SIM_SLOW_AFTER        (default 0)   — after this many reads, enter a
                                                "slow slave" episode. 0 = disabled.
  MODBUS_SIM_SLOW_DURATION_S   (default 30)  — how long an episode lasts.
  MODBUS_SIM_SLOW_EXTRA_MS     (default 100) — extra delay added during episode.

Set MODBUS_SIM_JITTER_MS=0 for deterministic test runs (the existing
ModbusTcpF1IntegrationTests + ModbusTcpToMqttEndToEndTests assert values,
not timing — jitter doesn't break them, but tests run faster without it).

Connection cap (typical S7-1200 limit ~3 concurrent clients) is NOT
enforced by this sim. To exercise that behaviour, validate against a
real PLC in Phase A''.

=== Tag map (unit id = 1) =====================================================

COILS (FC01) — zero-based:
  0: running
  1: alarm_active
  2: door_closed           (discrete inputs mirror semantics)

DISCRETE INPUTS (FC02) — zero-based:
  0: door_closed
  1: tool_in_spindle

HOLDING REGISTERS (FC03) — zero-based, big-endian per Modbus wire:
  0:     spindle_rpm       uint16  ABCD ignored (2-byte)     rpm
  1:     spindle_load      int16   (signed, -100..100)        %
  10-11: feed_rate         float32 ABCD                       mm/min
  20-21: parts_count       uint32 CDAB (word-swapped)         -
  30-31: cycle_time        float32 ABCD                       s
  40-41: energy_kwh        float32 ABCD                       kWh
  50:    alarm_code        int16                              -
  60-67: mode              string16 (8 chars)                 -
  100-103: part_name       string8  (8 chars)                 -

INPUT REGISTERS (FC04) — zero-based:
  0:     temperature       int16, scale 0.1                   °C (raw 420 → 42.0)
"""

import logging
import os
import random
import signal
import struct
import sys
import threading
import time

from pymodbus.datastore import (
    ModbusSequentialDataBlock,
    ModbusServerContext,
    ModbusSlaveContext,
)
from pymodbus.server import StartTcpServer

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s %(levelname)s [modbus-sim] %(message)s",
)
log = logging.getLogger("modbus-sim")

# --- helpers --------------------------------------------------------------


def _pack_float32_abcd(value: float) -> tuple[int, int]:
    """Split float32 into two big-endian uint16 registers (ABCD order)."""
    raw = struct.pack(">f", value)
    high = int.from_bytes(raw[0:2], "big")
    low = int.from_bytes(raw[2:4], "big")
    return high, low


def _pack_uint32_cdab(value: int) -> tuple[int, int]:
    """Word-swapped big-endian (CDAB) packing used by e.g. Schneider PLCs."""
    raw = struct.pack(">I", value & 0xFFFFFFFF)
    high = int.from_bytes(raw[0:2], "big")
    low = int.from_bytes(raw[2:4], "big")
    # CDAB → low word first on the wire
    return low, high


def _pack_string(text: str, chars: int) -> list[int]:
    """ASCII string, 2 chars per register, high-char-first, space-padded to `chars`."""
    padded = text.ljust(chars, " ")[:chars]
    regs: list[int] = []
    for i in range(0, len(padded), 2):
        high = ord(padded[i])
        low = ord(padded[i + 1]) if i + 1 < len(padded) else 0x20
        regs.append((high << 8) | low)
    return regs


def _env_int(name: str, default: int) -> int:
    raw = os.environ.get(name)
    if raw is None or raw == "":
        return default
    try:
        return int(raw)
    except ValueError:
        log.warning("env %s=%r is not an int; using default %d", name, raw, default)
        return default


# --- S7-1200 flavoured slave context --------------------------------------


class _FlavouredSlaveContext(ModbusSlaveContext):
    """
    ModbusSlaveContext wrapper that applies per-read jitter and an optional
    "slow slave" episode mode. Keeps the test-side determinism opt-in via
    env vars (set MODBUS_SIM_JITTER_MS=0 for zero jitter).
    """

    def __init__(self, *args, jitter_ms: int, slow_after: int,
                 slow_duration_s: int, slow_extra_ms: int, **kwargs) -> None:
        super().__init__(*args, **kwargs)
        self._jitter_ms = jitter_ms
        self._slow_after = slow_after
        self._slow_duration_s = slow_duration_s
        self._slow_extra_ms = slow_extra_ms
        self._req_count = 0
        self._slow_until_ts = 0.0
        self._lock = threading.Lock()

    def _maybe_delay(self) -> None:
        # Per-read uniform jitter — tiny, non-blocking enough to not warp test wall-clocks.
        if self._jitter_ms > 0:
            time.sleep(random.uniform(0, self._jitter_ms / 1000.0))

        # Episode trigger / membership check.
        in_slow = False
        with self._lock:
            self._req_count += 1
            now = time.monotonic()
            if (self._slow_after > 0
                    and self._req_count >= self._slow_after
                    and now > self._slow_until_ts):
                self._slow_until_ts = now + self._slow_duration_s
                self._req_count = 0
                log.info(
                    "slow-slave episode triggered (extra %dms for %ds)",
                    self._slow_extra_ms, self._slow_duration_s)
            in_slow = now < self._slow_until_ts

        if in_slow and self._slow_extra_ms > 0:
            time.sleep(self._slow_extra_ms / 1000.0)

    def getValues(self, fx, address, count=1):  # noqa: N802 — pymodbus API
        self._maybe_delay()
        return super().getValues(fx, address, count)


# --- initial dataset ------------------------------------------------------


def build_context() -> tuple[ModbusServerContext, _FlavouredSlaveContext]:
    coils = ModbusSequentialDataBlock(
        0,
        [True, False, True] + [False] * (2000 - 3),
    )
    discrete_inputs = ModbusSequentialDataBlock(
        0,
        [True, True] + [False] * (2000 - 2),
    )

    holding = [0] * 200
    holding[0] = 1450       # spindle_rpm
    holding[1] = 0xFFF1     # spindle_load int16 == -15
    holding[10], holding[11] = _pack_float32_abcd(250.5)        # feed_rate
    holding[20], holding[21] = _pack_uint32_cdab(1_234_567)      # parts_count (CDAB)
    holding[30], holding[31] = _pack_float32_abcd(42.75)        # cycle_time
    holding[40], holding[41] = _pack_float32_abcd(128.4)        # energy_kwh
    holding[50] = 0          # alarm_code
    # mode @ 60..67 (8 chars)
    for idx, reg in enumerate(_pack_string("AUTO", 8)):
        holding[60 + idx] = reg
    # part_name @ 100..103 (8 chars)
    for idx, reg in enumerate(_pack_string("SHAFT-7X", 8)):
        holding[100 + idx] = reg
    holding_block = ModbusSequentialDataBlock(0, holding)

    input_regs = [0] * 100
    input_regs[0] = 420      # temperature, scale 0.1 → 42.0 C
    input_block = ModbusSequentialDataBlock(0, input_regs)

    slave = _FlavouredSlaveContext(
        di=discrete_inputs,
        co=coils,
        hr=holding_block,
        ir=input_block,
        zero_mode=True,
        jitter_ms=_env_int("MODBUS_SIM_JITTER_MS", 5),
        slow_after=_env_int("MODBUS_SIM_SLOW_AFTER", 0),
        slow_duration_s=_env_int("MODBUS_SIM_SLOW_DURATION_S", 30),
        slow_extra_ms=_env_int("MODBUS_SIM_SLOW_EXTRA_MS", 100),
    )
    return ModbusServerContext(slaves={1: slave}, single=False), slave


# --- mutation thread ------------------------------------------------------


def _randomize_loop(slave: ModbusSlaveContext, stop: threading.Event) -> None:
    """
    Drive small random walks on selected tags every second so test
    subscribers see fresh data across multiple polls. Keeps values within
    realistic ranges.
    """
    while not stop.wait(1.0):
        try:
            hr = slave.store["h"]  # holding-register datablock
            ir = slave.store["i"]  # input-register datablock

            # spindle_rpm — walk within 1200..1600
            rpm = hr.getValues(0, 1)[0]
            rpm = max(1200, min(1600, rpm + random.randint(-25, 25)))
            hr.setValues(0, [rpm])

            # spindle_load — walk within -30..30 (store as signed via uint16 two's complement)
            load = hr.getValues(1, 1)[0]
            load_signed = load if load < 0x8000 else load - 0x10000
            load_signed = max(-30, min(30, load_signed + random.randint(-3, 3)))
            hr.setValues(1, [load_signed & 0xFFFF])

            # feed_rate float32 — ±5 mm/min jitter around 250
            current_bytes = struct.pack(">HH", *hr.getValues(10, 2))
            (current_feed,) = struct.unpack(">f", current_bytes)
            current_feed = max(200.0, min(300.0, current_feed + random.uniform(-5.0, 5.0)))
            new_high, new_low = _pack_float32_abcd(current_feed)
            hr.setValues(10, [new_high, new_low])

            # parts_count — monotonic increment (CDAB packed)
            cur_low, cur_high = hr.getValues(20, 2)
            count = ((cur_high << 16) | cur_low) + 1
            new_low, new_high = _pack_uint32_cdab(count)
            # note: _pack_uint32_cdab returns (low, high); setValues wants the
            # raw register pair in address order → (reg20, reg21)
            hr.setValues(20, [new_low, new_high])

            # energy_kwh — slow monotonic accumulator
            cur_bytes = struct.pack(">HH", *hr.getValues(40, 2))
            (kwh,) = struct.unpack(">f", cur_bytes)
            kwh = kwh + random.uniform(0.001, 0.005)
            nh, nl = _pack_float32_abcd(kwh)
            hr.setValues(40, [nh, nl])

            # temperature — walk within 380..460 raw (38.0..46.0 °C scaled)
            temp_raw = ir.getValues(0, 1)[0]
            temp_raw = max(380, min(460, temp_raw + random.randint(-2, 2)))
            ir.setValues(0, [temp_raw])
        except Exception as ex:  # pragma: no cover — defensive; keep the sim alive
            log.warning("randomize loop error: %s", ex)


# --- main ----------------------------------------------------------------


def main() -> int:
    port = _env_int("MODBUS_PORT", 5020)
    context, slave = build_context()
    log.info(
        "pymodbus TCP sim listening on 0.0.0.0:%d (unit id 1; jitter %dms; "
        "slow-after %d req → +%dms for %ds)",
        port,
        slave._jitter_ms,
        slave._slow_after,
        slave._slow_extra_ms,
        slave._slow_duration_s,
    )

    stop = threading.Event()

    def _terminate(signum, _frame):  # pragma: no cover
        log.info("Received signal %s — shutting down", signum)
        stop.set()
        sys.exit(0)

    signal.signal(signal.SIGTERM, _terminate)
    signal.signal(signal.SIGINT, _terminate)

    # Daemon thread so the pymodbus event loop can exit cleanly on SIGTERM.
    randomizer = threading.Thread(
        target=_randomize_loop, args=(slave, stop), name="modbus-randomizer", daemon=True)
    randomizer.start()

    StartTcpServer(context=context, address=("0.0.0.0", port))
    return 0


if __name__ == "__main__":
    sys.exit(main())
