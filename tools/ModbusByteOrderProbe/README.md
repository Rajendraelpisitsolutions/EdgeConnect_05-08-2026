# ModbusByteOrderProbe

Operator commissioning aid. Solves the single most common Modbus
integration headache: **"the value comes through wrong on first connect
and I don't know which `byteOrder` to use."**

Connects to a Modbus TCP target, reads N registers from one address,
and decodes the same bytes under every supported byte order. Operator
picks the row whose decoded value matches the PLC's HMI / engineering
tool. That `byteOrder` value goes straight into the gateway config.

## Usage

```
ModbusByteOrderProbe --host <ip|hostname> [--port 502]
                     [--unit 1] --address <reg> [--width 2]
                     --datatype <name>
```

Required: `--host`, `--address`, `--datatype`. The rest have sensible
defaults.

## Example session

You have a Siemens S7-1200 at `192.168.1.50` exposing a `feed_rate`
float32 at holding-register address 10. The HMI shows `250.5`. You're
not sure if it's ABCD or CDAB:

```
ModbusByteOrderProbe --host 192.168.1.50 --address 10 --width 2 --datatype float32
```

```
Connecting to 192.168.1.50:502 (unitId=1)...

Raw registers (high-byte-first per register, as Modbus wire delivered):
  index  hex     dec
     0    0x437A   17274
     1    0x8000   32768

Decoded under each byte order (float32, 2 register(s)):

  byteOrder    decoded value
  -----------  -----------------------------
  ABCD         250.5
  CDAB         8.022568E+33
  BADC         -7.853398E-19
  DCBA         -3.4028235E+38

Pick the row whose decoded value matches the PLC HMI / engineering tool.
Use that byteOrder in your gateway.json tag definition.
```

The HMI shows `250.5` → use `byteOrder: "ABCD"`. Done.

## Datatype → width quick reference

| Datatype  | `--width` | Byte orders shown |
|-----------|-----------|-------------------|
| `uint16`, `int16`               | 1 | AB, BA |
| `uint32`, `int32`, `float32`    | 2 | ABCD, CDAB, BADC, DCBA |
| `uint64`, `int64`, `float64`    | 4 | ABCDEFGH, HGFEDCBA |

The probe automatically restricts the comparison table to byte orders
that match the requested datatype's width.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | success — table printed |
| 1 | connect or read error (printed to stderr) |
| 2 | argument error |

## Why this exists

Every shipped Modbus integration eventually hits a tag whose value
reads as nonsense (negative when it should be positive, 1.4e-39 when
it should be 250.5). It's almost always wrong byte order. With this
tool, identifying the right order is one shell command instead of N
gateway-config edit cycles.
