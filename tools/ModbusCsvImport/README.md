# ModbusCsvImport

Command-line companion to F4's `ModbusTagCsvImporter`. Turns a CSV of
Modbus tag definitions into a JSON fragment you can paste directly under
`sources[].connection.tagDefinitions` in a gateway config.

Pure text transform — the tool **never** mutates an existing gateway
config. Phase 4's management API will call the same underlying library.

## Usage

```bash
ModbusCsvImport --csv plc-line1.csv
# writes plc-line1.tags.json next to the input

ModbusCsvImport --csv plc-line1.csv --output - --pretty > fragment.json
# emit indented JSON to stdout for piping

ModbusCsvImport --csv bad.csv
# prints all errors to stderr and exits non-zero
```

## CSV schema

Header row required. Column order is flexible; column names are
case-insensitive.

| Column | Required | Notes |
|---|---|---|
| `name` | yes | Tag name emitted on MQTT + Tag Catalog |
| `unitId` | yes | Slave id 0..247 |
| `registerClass` | yes | `Coil` / `DiscreteInput` / `HoldingRegister` / `InputRegister` |
| `address` | yes | Zero-based logical address. **Do NOT use 40001/30001 vendor notation.** |
| `datatype` | yes | `bool`, `int16/uint16/int32/uint32/int64/uint64`, `float32/float64`, `stringN` (e.g. `string16`) |
| `scanRateMs` | yes | Poll period in milliseconds |
| `byteOrder` | no  | `AB` / `BA` / `ABCD` / `CDAB` / `BADC` / `DCBA` / `ABCDEFGH` / `HGFEDCBA` — default is big-endian at the datatype's width |
| `scale` | no  | Linear scale; rejected for `bool` / `stringN` |
| `offset` | no  | Additive offset; rejected for `bool` / `stringN` |
| `unit` | no  | Engineering unit string (surfaced on `CanonicalDataPoint.Unit`) |
| `description` | no  | Free-form human description (surfaced on `/api/tags` metadata in Phase 4) |

Lines starting with `#` are full-line comments and ignored. Blank lines
are ignored. UTF-8 with or without BOM both work.

## Validation

Strict: the whole file fails on any error. All errors are reported in one
run — no need to fix-and-retry one row at a time.

Enforced rules (the importer, adapter's `ValidateConfigAsync`, and Phase 4
management API share the same `ModbusTagValidator`):

- `bool` datatype only on `Coil` / `DiscreteInput`; rejected on registers
- register datatypes rejected on `Coil` / `DiscreteInput`
- `byteOrder` width must match datatype width (e.g. `AB` with `float32` is rejected)
- `byteOrder` on bit classes is rejected
- `scale` / `offset` rejected on `bool` / `stringN`
- duplicate tag names across rows are rejected
- addresses in the 10001-19999 / 30001-49999 range are rejected as legacy vendor notation
- unknown header columns are rejected

Non-fatal warnings (printed to stderr; don't fail the import):

- two or more tags sharing a register range — allowed (e.g. raw + bitfield overlay) but usually unintentional

## Built-in templates

Two reference templates are shipped as embedded resources inside
`ElpisEdgeConnect.Sources.ModbusTcp`:

- `generic-plc` — plant-floor PLC shape (production counter, energy, faults)
- `cnc-via-modbus-gateway` — CNC exposed via a Modbus bridge (spindle, feed, tool, program)

Load them programmatically via `ModbusTagTemplate.LoadCsv("generic-plc")`,
or copy the CSV files out of
`src/ElpisEdgeConnect.Sources.ModbusTcp/Templates/` and edit them to
match your specific device.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | success |
| 1 | validation errors (printed to stderr) |
| 2 | argument / I/O error |
