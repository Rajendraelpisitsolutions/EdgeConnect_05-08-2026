# Sparkplug B `sparkplug_b.proto` — Provenance and Generation Record

**Created:** 2026-07-19 (K2 Slice 1)
**Governing decision:** ADR-0035 Rule 2 — no Eclipse Tahu **runtime** dependency;
payload types are generated from a pinned, reviewed copy of the official schema.
**Scope of this document:** it records provenance and the redistribution actions
taken. It makes **no legal conclusion** about license obligations of the schema
or generated code — those determinations go through the project's
open-source-compliance review process.

## Pinned schema

| Item | Value |
|---|---|
| Upstream repository | `https://github.com/eclipse-tahu/tahu` |
| Upstream path | `sparkplug_b/sparkplug_b.proto` |
| **Pinned commit** | `46f25e79f34234e6145d11108660dfd9133ae50d` (2022-05-16, last upstream change to the file) |
| **SHA-256** | `4432C5C483B7FB9732D0594C98A2E97DCA5E517E39C5374A8B918D837F0B4A19` |
| Size | 8,330 bytes |
| Retrieved | 2026-07-19, byte-exact from `raw.githubusercontent.com` at the pinned commit |
| Vendored at | `src/ElpisEdgeConnect.Sinks.SparkplugB/Protos/sparkplug_b.proto` (unmodified; upstream copyright header, `SPDX-License-Identifier: EPL-2.0`, and contributor notice intact) |
| Schema license | EPL-2.0 (per the upstream SPDX header) |
| Syntax | `proto2`, package `org.eclipse.tahu.protobuf` |

## Generation toolchain

| Item | Value |
|---|---|
| Compiler | `libprotoc 35.1` (from NuGet package `Google.Protobuf.Tools` **3.35.1**) |
| Runtime library | `Google.Protobuf` **3.35.1** (the only package the sink assembly references for payloads) |
| Command | `protoc --proto_path=<Protos> --csharp_out=<out> --csharp_opt=internal_access,file_extension=.g.cs sparkplug_b.proto` |
| Output | `src/ElpisEdgeConnect.Sinks.SparkplugB/Protobuf/SparkplugB.g.cs` (vendored, `internal` visibility, auto-generated header; **never hand-edited**) |
| **Generated-file SHA-256** | `84E844E7CB5E6B369E49E071CD45F0AE961EA922BAEB95302F27449E3F5529C7` |
| Generated-file size | 296,399 bytes |
| Script | `tools/sparkplug-proto/regenerate.ps1` (the single source of the generation command and pins) |
| Regeneration hosts verified | **Windows x64 only** (Slice 1, 2026-07-19). The script maps Linux/macOS x64/arm64 paths and rejects other architectures, but cross-platform regeneration has not been exercised — verify on first use. |

**Integrity chain:** pinned proto hash → pinned compiler/tool version →
deterministic regeneration (`-Verify` compares **SHA-256 of the bytes**, so
encoding-level differences fail) → pinned generated-file hash (also asserted by
`ProtoProvenanceTests`). The static hashes complement — never replace — the
regeneration comparison.

## Verification procedure

`pwsh tools/sparkplug-proto/regenerate.ps1 -Verify` fails (exit 1) when:

1. the vendored `.proto`'s SHA-256 differs from the pinned value above (the
   schema was edited or replaced), or
2. regenerating from the pinned schema with the pinned toolchain produces
   output whose **SHA-256 differs byte-level** from the vendored
   `SparkplugB.g.cs` (the generated code was edited, its encoding changed, or
   the toolchain drifted).

`ProtoProvenanceTests` additionally asserts the SHA-256 **and byte length** of
both the schema and the vendored generated file at test time (copied to test
output with `CopyToOutputDirectory="Always"` so a stale copy can never be
inspected), so the gate `dotnet test` run also fails on drift of either file.

## Redistribution actions taken

- The unmodified EPL-2.0 schema is redistributed in this repository with its
  upstream license header intact.
- Generated C# derived from that schema is redistributed in this repository,
  marked auto-generated, with this record identifying its exact origin.
- SBOM treatment, notice-file inclusion for shipped artifacts, and any further
  obligations are handled by the open-source-compliance review before ship
  (tracked as an ADR-0035 Rule 2 pre-ship item; cf. ADR-0033 posture).

## Re-pinning procedure

To adopt a newer upstream schema: update the pinned commit + SHA-256 here **and**
in `regenerate.ps1` in the same change, re-run the script, review the generated
diff, update the generated-file SHA-256/size here and in
`ProtoProvenanceTests`, and re-run the full golden conformance suite. Never
edit the vendored `.proto` or `.g.cs` directly.
