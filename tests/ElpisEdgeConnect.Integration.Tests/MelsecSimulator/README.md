# MELSEC / SLMP MC-3E binary simulator (dev tooling)

A standalone, pure-standard-library Python simulator that speaks the **exact
and only** frame the EdgeConnect Slice-1 MELSEC adapter uses: SLMP / MC
Protocol **3E binary**, **batch read, word units** (command `0x0401`,
subcommand `0x0000`), read-only.

> **Not product scope, not in the .NET solution.** This is a dev/soak aid,
> the MELSEC counterpart to the sibling [`../ModbusSimulator`](../ModbusSimulator).
> No pip install, no third-party MELSEC package — matching the project's
> hand-rolled-wire ethos. The wire layout is byte-for-byte matched to
> `src/ElpisEdgeConnect.Sources.Melsec/Wire/SlmpFrameCodec.cs`.

> **Field-verification caveat:** device-code bytes and framing here are
> spec-derived (Mitsubishi SH(NA)-080008) — the *same* assumptions the adapter
> encodes. A green run proves the adapter and this sim agree; it does **not**
> replace the customer **Part B** known-good capture that verifies golden
> bytes, word order, and bit-device alignment against real hardware.

## Run

```powershell
# from tests/ElpisEdgeConnect.Integration.Tests/MelsecSimulator
py server.py            # or:  python server.py
```

No venv or dependencies required (standard library only). Listens on
`0.0.0.0:5007` by default. Then in the Studio (`127.0.0.1:5080`):
**Add source → Mitsubishi MELSEC**, host `127.0.0.1`, port `5007`, and use
**Test connection** / **Test read** / (after apply) the SourceDetail
**MELSEC diagnostics** panel.

### Family profiles (A-2 Gate A-2S)

The simulator can enforce a family profile, mirroring the driver's profile
registry (`Profiles/MelsecProfiles.cs`):

```powershell
py server.py --profile fx5          # CLI flag (wins)
$env:MELSEC_SIM_PROFILE = "fx5"; py server.py   # env fallback; default = modern
```

| Profile | Devices served | Cap | Behavior |
|---|---|---|---|
| `modern` (default) | D W R ZR M X Y B | 960 words | as before |
| `fx5` | D W R M X Y B (**no ZR** — FX5 CPU cannot access it) | 960 words | ZR reads answered with end code `0xC059` |

Both profiles reject reads above the 960-word cap with end code `0xC056`.
Sim convention note: a real FX5 may use a different end code for an
inaccessible device — the sim's `0xC059` is a documented stand-in, not field
truth. Self-test: `py verify.py --profile fx5` (or `--profile modern`).

### Environment variables

| Env var | Default | Effect |
|---|---|---|
| `MELSEC_PORT`          | `5007` | TCP listen port. |
| `MELSEC_SIM_PROFILE`   | `modern` | Family profile (`modern` / `fx5`); the `--profile` CLI flag wins. |
| `MELSEC_SIM_JITTER_MS` | `5`    | Uniform delay per read. Set `0` for deterministic runs. |
| `MELSEC_SIM_END_CODE`  | `0`    | If non-zero (e.g. `0xC056`), every read returns this MELSEC end code instead of data — exercises the adapter's protocol-error path + diagnostics `LastEndCode`. |

Any non-batch-read-word frame (or unknown device code) is answered with end
code `0xC059` (command error), so the adapter sees a clean protocol error
rather than a hang.

## Seeded address map

Any address **not** listed returns `word_index & 0xFFFF` (so reading `D250`
yields `250` — a quick way to confirm the wire resolved the right
device+offset). Seeded numeric tags marked *walks* drift ±3 each second.

| Address | Device radix | Datatype (suggested) | Word order | Value |
|---|---|---|---|---|
| `D0`        | decimal | UInt16  | —          | 1450 *(walks)* |
| `D1`        | decimal | Int16   | —          | -15 *(walks)* |
| `D10`       | decimal | Float32 | LowWordFirst | 250.5 |
| `D20`       | decimal | UInt32  | LowWordFirst | 1 234 567 |
| `D100`      | decimal | Int16   | —          | 42 *(walks)* |
| `W1A`       | **hex** | UInt16  | —          | 0x00FF |
| `ZR16384`   | **hex** (`0x4000`) | Int16 | — | 777 |
| `M0`        | decimal bit | Bool | —          | true (bit0 of the M0 word) |

Radix reminder (per adapter): **hex** devices `X Y B W SB SW DX DY ZR`;
**decimal** devices `M L F V S T C D R SM SD Z`. Slice-1 *implemented* devices
are `D W R ZR` (word) and `M X Y B` (bit via word-units) only.

## Quick verification (no Studio needed)

```powershell
py verify.py       # builds a 0401 request, prints decoded D0/D10/D20
```

If `verify.py` is absent, this one-liner does the same against a running sim:

```python
import socket, struct
s = socket.create_connection(("127.0.0.1", 5007))
def read(dev, head, pts):
    req = struct.pack("<BBBBHB HHH", 0x50,0,0,0xFF,0x03FF,0, 0, 0x0401,0x0000) \
          + bytes([head & 0xFF,(head>>8)&0xFF,(head>>16)&0xFF, dev]) + struct.pack("<H", pts)
    s.sendall(req)
    hdr = s.recv(9); rlen = hdr[7]|hdr[8]<<8; data = s.recv(rlen)
    ec = data[0]|data[1]<<8
    return ec, [w[0] for w in struct.iter_unpack("<H", data[2:])]
print("D0  ->", read(0xA8, 0, 1))          # (0, [1450±])
print("D10 ->", read(0xA8, 10, 2))         # (0, [32768, 17274]) == 250.5
s.close()
```

## Docker (optional)

```bash
docker build -t elpis-melsec-sim:dev .
docker run --rm -p 5007:5007 elpis-melsec-sim:dev
```

## What's NOT simulated

- **Connection cap** — real Ethernet modules limit concurrent SLMP sessions;
  this sim accepts unlimited connections.
- **Random disconnects / TCP RST**, monitoring-timer semantics, 4E/1E/ASCII/UDP,
  writes — all out of Slice-1 scope by design.
