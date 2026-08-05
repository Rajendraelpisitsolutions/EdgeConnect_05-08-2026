#!/usr/bin/env python3
# ============================================================================
# Standalone MELSEC / SLMP MC-3E BINARY simulator (dev tooling — NOT product
# scope, NOT part of the .NET solution). Read-only batch-read (command 0x0401,
# word-units subcommand 0x0000) — the exact and only frame the EdgeConnect
# Slice-1 MELSEC adapter speaks.
#
# Pure Python standard library — no pip install, no third-party MELSEC package
# (mirrors the project's hand-rolled-wire ethos). Mirrors the sibling
# ModbusSimulator/ pattern: run server.py, point the Studio MELSEC wizard at
# 127.0.0.1:<port>, exercise Test connection / Test read / live diagnostics.
#
# Wire layout is byte-for-byte matched to src/…/Wire/SlmpFrameCodec.cs:
#   Request  (21B): 50 00 | net pc ioLo ioHi station | lenLo lenHi
#                   | timerLo timerHi | 01 04 | 00 00 | headLo headMid headHi | dev | cntLo cntHi
#   Response(11+2n): D0 00 | net pc ioLo ioHi station | rlenLo rlenHi
#                   | ecLo ecHi | <n LE words>
#
# GOLDEN/SPEC NOTE: device-code bytes + framing are spec-derived
# (Mitsubishi SH(NA)-080008) and remain FIELD-UNVERIFIED until the customer
# Part B capture. This simulator encodes the SAME spec assumptions as the
# adapter, so a green run here proves internal consistency, NOT field truth.
# ============================================================================

import argparse
import os
import socket
import socketserver
import struct
import threading
import time

# ── Device-code bytes (must match Wire/MelsecDeviceCode.cs) ────────────────
DEV = {0xA8: "D", 0xB4: "W", 0xAF: "R", 0xB0: "ZR", 0x90: "M", 0x9C: "X", 0x9D: "Y", 0xA0: "B",
       # A-3a special devices ([MC] SH-080008-AB p68 codes; on both profiles)
       0x91: "SM", 0xA9: "SD", 0xA1: "SB", 0xB5: "SW",
       # A-3b timers/counters (both profiles): TS/TC/TN STS/STC/STN CS/CC/CN
       0xC1: "TS", 0xC0: "TC", 0xC2: "TN", 0xC7: "STS", 0xC6: "STC", 0xC8: "STN",
       0xC4: "CS", 0xC3: "CC", 0xC5: "CN"}
CODE = {name: code for code, name in DEV.items()}

CMD_BATCH_READ = 0x0401
SUBCMD_WORD = 0x0000

# ── Family profiles (A-2 Gate A-2S; mirrors Profiles/MelsecProfiles.cs) ────
# fx5: ZR is not accessible on the FX5 CPU (Gate A-2D audit); reads against it
# answer with a protocol error instead of data. Cap is 960 words for both
# (SH-082625ENG-J §38.1 confirmed FX5 = 960, same as Modern).
PROFILES = {
    "modern": {"devices": set(DEV), "cap": 960},
    "fx5": {"devices": set(DEV) - {CODE["ZR"]}, "cap": 960},
}
# Selection: CLI --profile wins over the MELSEC_SIM_PROFILE env var.
PROFILE_NAME = "modern"

# End codes (0 = success). Override via MELSEC_SIM_END_CODE to exercise the
# adapter's protocol-error path (e.g. 0xC056 = read address+points out of range).
END_SUCCESS = 0x0000
FORCED_END_CODE = int(os.environ.get("MELSEC_SIM_END_CODE", "0"), 0)
# 0xC059 command/subcommand error — returned for any non batch-read-word frame
# and (sim convention, documented in README) for devices outside the active
# profile's set, e.g. ZR in fx5 mode.
END_COMMAND_ERROR = 0xC059
# 0xC056 — points/range exceeded; returned when a read exceeds the profile cap.
END_RANGE_ERROR = 0xC056

PORT = int(os.environ.get("MELSEC_PORT", "5007"))
JITTER_MS = int(os.environ.get("MELSEC_SIM_JITTER_MS", "5"))

# ── Data model: (device_code, word_index) -> 16-bit value ──────────────────
# Seeded demonstrative map (see README). A background thread random-walks the
# numeric seeds each second so a trend-aware reader sees fresh data. Any address
# NOT seeded returns (word_index & 0xFFFF) — so reading e.g. D250 yields 250,
# a cheap way to confirm the wire path resolved the right device+offset.
_lock = threading.Lock()


def _f32_words(value, low_word_first=True):
    raw = struct.pack("<f", value)
    w0 = int.from_bytes(raw[0:2], "little")
    w1 = int.from_bytes(raw[2:4], "little")
    return (w0, w1) if low_word_first else (w1, w0)


def _u32_words(value, low_word_first=True):
    raw = struct.pack("<I", value & 0xFFFFFFFF)
    w0 = int.from_bytes(raw[0:2], "little")
    w1 = int.from_bytes(raw[2:4], "little")
    return (w0, w1) if low_word_first else (w1, w0)


D = CODE["D"]
M = CODE["M"]
W = CODE["W"]
ZR = CODE["ZR"]

_f = _f32_words(250.5)      # feed_rate  @ D10/D11  (Float32, LowWordFirst)
_u = _u32_words(1234567)    # parts_cnt  @ D20/D21  (UInt32,  LowWordFirst)

STORE = {
    (D, 0): 1450,           # spindle_rpm  UInt16
    (D, 1): (-15) & 0xFFFF,  # spindle_load Int16 (two's complement)
    (D, 10): _f[0], (D, 11): _f[1],
    (D, 20): _u[0], (D, 21): _u[1],
    (D, 100): 42,           # total_cycles Int16
    (W, 0x1A): 0x00FF,      # link register W1A (hex device)
    (ZR, 0x4000): 777,      # ZR16384 (0x4000 hex) — recipe slot
    (M, 0): 0x0001,         # bit word @ M0: bit0 set  -> M0 = true (running)
    # A-3a special-device seeds (deterministic; both profiles)
    (CODE["SM"], 0): 0x0001,   # SM0 word: bit0 ON
    (CODE["SD"], 0): 42,       # SD0 = 42
    (CODE["SB"], 0): 0x0001,   # SB0 word: bit0 ON
    (CODE["SW"], 0x10): 0x0AB0,  # SW10 (hex) = 0x0AB0
    # A-3b timer/counter seeds (deterministic; both profiles)
    (CODE["TN"], 100): 1234,   # timer 100 current value
    (CODE["TS"], 100): 0x0001,  # timer 100 contact word bit0 ON
    (CODE["CN"], 0): 7,        # counter 0 current value
    (CODE["CS"], 0): 0x0001,   # counter 0 contact word bit0 ON
    (CODE["STN"], 0): 500,     # retentive timer 0 current value
    (CODE["STS"], 0): 0x0001,  # retentive timer 0 contact word bit0 ON
}

# Keys the walker nudges (numeric, non-bit).
_WALK = [(D, 0), (D, 1), (D, 100)]


def _walker():
    import random
    rng = random.Random(1450)
    while True:
        time.sleep(1.0)
        with _lock:
            for key in _WALK:
                base = STORE.get(key, 0)
                if base & 0x8000:  # treat as signed
                    base -= 0x10000
                base += rng.randint(-3, 3)
                STORE[key] = base & 0xFFFF


def read_words(device_code, head, points):
    with _lock:
        return [STORE.get((device_code, head + i), (head + i) & 0xFFFF) for i in range(points)]


def build_response(net, pc, io_no, station, end_code, words):
    payload = b"".join(struct.pack("<H", w & 0xFFFF) for w in words)
    after_ec = payload if end_code == END_SUCCESS else b""
    rlen = 2 + len(after_ec)
    return struct.pack(
        "<BBBBHB H H",
        0xD0, 0x00, net, pc, io_no, station, rlen, end_code,
    ) + after_ec


def _recvall(sock, n):
    buf = bytearray()
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            return None
        buf.extend(chunk)
    return bytes(buf)


class Handler(socketserver.BaseRequestHandler):
    def handle(self):
        peer = self.client_address
        print(f"[+] connect {peer}")
        try:
            while True:
                header = _recvall(self.request, 9)
                if header is None:
                    break
                sub0, sub1 = header[0], header[1]
                net, pc = header[2], header[3]
                io_no = header[4] | (header[5] << 8)
                station = header[6]
                req_len = header[7] | (header[8] << 8)
                body = _recvall(self.request, req_len)
                if body is None:
                    break

                if (sub0, sub1) != (0x50, 0x00) or req_len < 12:
                    self.request.sendall(build_response(net, pc, io_no, station, END_COMMAND_ERROR, []))
                    continue

                cmd = body[2] | (body[3] << 8)
                subcmd = body[4] | (body[5] << 8)
                head = body[6] | (body[7] << 8) | (body[8] << 16)
                dev = body[9]
                points = body[10] | (body[11] << 8)

                if cmd != CMD_BATCH_READ or subcmd != SUBCMD_WORD or dev not in DEV:
                    print(f"    ! unsupported frame cmd=0x{cmd:04X} sub=0x{subcmd:04X} dev=0x{dev:02X}")
                    self.request.sendall(build_response(net, pc, io_no, station, END_COMMAND_ERROR, []))
                    continue

                profile = PROFILES[PROFILE_NAME]
                if dev not in profile["devices"]:
                    print(f"    ! device {DEV[dev]} not accessible in '{PROFILE_NAME}' profile -> end code 0x{END_COMMAND_ERROR:04X}")
                    self.request.sendall(build_response(net, pc, io_no, station, END_COMMAND_ERROR, []))
                    continue
                if points < 1 or points > profile["cap"]:
                    print(f"    ! {points} points exceeds profile cap {profile['cap']} -> end code 0x{END_RANGE_ERROR:04X}")
                    self.request.sendall(build_response(net, pc, io_no, station, END_RANGE_ERROR, []))
                    continue

                if JITTER_MS:
                    time.sleep(JITTER_MS / 1000.0)

                if FORCED_END_CODE != 0:
                    print(f"    -> forced end code 0x{FORCED_END_CODE:04X} for {DEV[dev]}{head} x{points}")
                    self.request.sendall(build_response(net, pc, io_no, station, FORCED_END_CODE, []))
                    continue

                words = read_words(dev, head, points)
                print(f"    read {DEV[dev]}{head} x{points} -> {words}")
                self.request.sendall(build_response(net, pc, io_no, station, END_SUCCESS, words))
        except (ConnectionError, OSError) as exc:
            print(f"[-] {peer} dropped: {exc}")
        finally:
            print(f"[-] disconnect {peer}")


class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True


def main():
    global PROFILE_NAME
    parser = argparse.ArgumentParser(description="MELSEC MC-3E binary simulator (dev tooling)")
    parser.add_argument("--profile", choices=sorted(PROFILES),
                        help="Family profile (CLI wins over MELSEC_SIM_PROFILE env; default modern)")
    args = parser.parse_args()
    env_profile = os.environ.get("MELSEC_SIM_PROFILE", "").strip().lower()
    PROFILE_NAME = args.profile or (env_profile if env_profile in PROFILES else "modern")

    threading.Thread(target=_walker, daemon=True).start()
    with Server(("0.0.0.0", PORT), Handler) as srv:
        prof = PROFILES[PROFILE_NAME]
        devs = ", ".join(sorted(DEV[c] for c in prof["devices"]))
        print(f"MELSEC MC-3E binary simulator listening on 0.0.0.0:{PORT}")
        print(f"  profile={PROFILE_NAME} devices=[{devs}] cap={prof['cap']} words")
        print(f"  jitter={JITTER_MS}ms forced_end_code=0x{FORCED_END_CODE:04X}")
        print("  Ctrl+C to stop.")
        try:
            srv.serve_forever()
        except KeyboardInterrupt:
            print("\nshutting down.")


if __name__ == "__main__":
    main()
