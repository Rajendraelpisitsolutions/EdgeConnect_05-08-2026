#!/usr/bin/env python3
# Quick self-test client for the MELSEC MC-3E binary simulator. Builds real
# 0401/word batch-read requests (byte-for-byte like SlmpFrameCodec.cs) and
# decodes the responses. Start server.py first, then run this.
import os
import socket
import struct
import sys

HOST = os.environ.get("MELSEC_HOST", "127.0.0.1")
PORT = int(os.environ.get("MELSEC_PORT", "5007"))
DEVCODE = {"D": 0xA8, "W": 0xB4, "R": 0xAF, "ZR": 0xB0, "M": 0x90, "X": 0x9C, "Y": 0x9D, "B": 0xA0,
           "SM": 0x91, "SD": 0xA9, "SB": 0xA1, "SW": 0xB5,
           "TS": 0xC1, "TC": 0xC0, "TN": 0xC2, "STS": 0xC7, "STC": 0xC6, "STN": 0xC8,
           "CS": 0xC4, "CC": 0xC3, "CN": 0xC5}


def read(sock, dev, head, points):
    req = (
        struct.pack("<BBBBHB", 0x50, 0x00, 0x00, 0xFF, 0x03FF, 0x00)  # subheader+route+...
        + struct.pack("<H", 12)                                        # request data length
        + struct.pack("<H", 0)                                         # monitoring timer
        + struct.pack("<HH", 0x0401, 0x0000)                           # command + subcommand
        + bytes([head & 0xFF, (head >> 8) & 0xFF, (head >> 16) & 0xFF, DEVCODE[dev]])
        + struct.pack("<H", points)                                    # point count
    )
    sock.sendall(req)
    hdr = sock.recv(9)
    rlen = hdr[7] | (hdr[8] << 8)
    data = b""
    while len(data) < rlen:
        data += sock.recv(rlen - len(data))
    end_code = data[0] | (data[1] << 8)
    words = [w[0] for w in struct.iter_unpack("<H", data[2:])]
    return end_code, words


def main():
    import argparse
    parser = argparse.ArgumentParser(description="MELSEC simulator self-test client")
    parser.add_argument("--profile", choices=["modern", "fx5"],
                        help="Expected server profile (CLI wins over MELSEC_SIM_PROFILE env; default modern)")
    args = parser.parse_args()
    env_profile = os.environ.get("MELSEC_SIM_PROFILE", "").strip().lower()
    profile = args.profile or (env_profile if env_profile in ("modern", "fx5") else "modern")

    failures = []

    def check(label, ok, detail):
        print(f"{'ok ' if ok else 'FAIL'} {label}  {detail}")
        if not ok:
            failures.append(label)

    with socket.create_connection((HOST, PORT), timeout=3) as s:
        ec, w = read(s, "D", 0, 1);   check("D0", ec == 0, f"ec=0x{ec:04X} words={w} (spindle_rpm ~1450)")
        ec, w = read(s, "D", 10, 2)
        f = struct.unpack("<f", struct.pack("<HH", *w))[0] if ec == 0 and len(w) == 2 else None
        check("D10", ec == 0 and f == 250.5, f"ec=0x{ec:04X} float32={f} (expect 250.5)")
        ec, w = read(s, "D", 20, 2)
        u = struct.unpack("<I", struct.pack("<HH", *w))[0] if ec == 0 and len(w) == 2 else None
        check("D20", ec == 0 and u == 1234567, f"ec=0x{ec:04X} uint32={u} (expect 1234567)")
        ec, w = read(s, "W", 0x1A, 1); check("W1A", ec == 0 and w == [255], f"ec=0x{ec:04X} words={w} (expect [255])")
        ec, w = read(s, "M", 0, 1);   check("M0", ec == 0 and w and w[0] & 1, f"ec=0x{ec:04X} word=0x{(w[0] if w else 0):04X} (bit0 -> true)")
        # X10 (octal operator label on FX5) = numeric point 8 -> head 8. The wire
        # is numeric on BOTH profiles; only operator-side parsing differs.
        ec, w = read(s, "X", 8, 1);   check("X(head 8)", ec == 0, f"ec=0x{ec:04X} words={w} (unseeded -> 8)")
        # A-3a special devices (seeded on both profiles)
        ec, w = read(s, "SM", 0, 1);  check("SM0", ec == 0 and w and w[0] & 1, f"ec=0x{ec:04X} word=0x{(w[0] if w else 0):04X} (bit0 ON)")
        ec, w = read(s, "SD", 0, 1);  check("SD0", ec == 0 and w == [42], f"ec=0x{ec:04X} words={w} (expect [42])")
        ec, w = read(s, "SB", 0, 1);  check("SB0", ec == 0 and w and w[0] & 1, f"ec=0x{ec:04X} word=0x{(w[0] if w else 0):04X} (bit0 ON)")
        ec, w = read(s, "SW", 0x10, 1); check("SW10", ec == 0 and w == [0x0AB0], f"ec=0x{ec:04X} words={w} (expect [2736])")
        # A-3b timers/counters (seeded both profiles)
        ec, w = read(s, "TN", 100, 1); check("TN100", ec == 0 and w == [1234], f"ec=0x{ec:04X} words={w} (expect [1234])")
        ec, w = read(s, "TS", 100, 1); check("TS100", ec == 0 and w and w[0] & 1, f"ec=0x{ec:04X} word=0x{(w[0] if w else 0):04X} (bit0 ON)")
        ec, w = read(s, "CN", 0, 1);   check("CN0", ec == 0 and w == [7], f"ec=0x{ec:04X} words={w} (expect [7])")
        ec, w = read(s, "STN", 0, 1);  check("STN0", ec == 0 and w == [500], f"ec=0x{ec:04X} words={w} (expect [500])")

        if profile == "fx5":
            # ZR must answer with a protocol error (not data): FX5 CPU cannot access ZR.
            ec, w = read(s, "ZR", 0x4000, 1)
            check("ZR rejected", ec != 0, f"ec=0x{ec:04X} (expect non-zero end code in fx5 profile)")
        else:
            ec, w = read(s, "ZR", 0x4000, 1)
            check("ZR16384", ec == 0 and w == [777], f"ec=0x{ec:04X} words={w} (expect [777])")

        # Cap enforcement: 961 words must exceed the 960 cap on both profiles.
        ec, w = read(s, "D", 0, 961)
        check("cap 961", ec != 0, f"ec=0x{ec:04X} (expect non-zero: exceeds 960-word cap)")

    if failures:
        print(f"FAILED: {failures}", file=sys.stderr)
        sys.exit(1)
    print(f"OK ({profile} profile)")


if __name__ == "__main__":
    try:
        main()
    except OSError as exc:
        print(f"connect/read failed: {exc}", file=sys.stderr)
        sys.exit(1)
