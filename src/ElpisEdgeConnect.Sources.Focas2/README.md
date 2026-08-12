# ElpisEdgeConnect.Sources.Focas2

FOCAS2 source adapter for Fanuc CNC controllers. Implements
`Core.Adapters.ISourceAdapter`; emits canonical data points via the
standard Phase 1 pipeline.

## Quick links

- **Full deployment & configuration guide:** [`docs/adapter-sdk/focas2-adapter.md`](../../docs/adapter-sdk/focas2-adapter.md)
- **Contract this adapter implements:** [`docs/adapter-sdk/source-adapter-contract.md`](../../docs/adapter-sdk/source-adapter-contract.md)
- **End-to-end integration test:** [`tests/ElpisEdgeConnect.Integration.Tests/Focas2ToMqttEndToEndTests.cs`](../../tests/ElpisEdgeConnect.Integration.Tests/Focas2ToMqttEndToEndTests.cs)
- **Unit tests (75):** [`tests/ElpisEdgeConnect.Sources.Focas2.Tests/`](../../tests/ElpisEdgeConnect.Sources.Focas2.Tests/)

## Build

```bash
dotnet build src/ElpisEdgeConnect.Sources.Focas2/ElpisEdgeConnect.Sources.Focas2.csproj
```

Targets `net8.0`. Runs on Windows (x64 and Arm64) and Linux (x64). The
native `Fwlib64.dll` / `libfwlib32.so` is only needed at **runtime**,
not at build time — `DllImport` binds lazily.

## Test

```bash
# Unit tests (no CNC, no DLL needed — uses FakeFocas2Api)
dotnet test tests/ElpisEdgeConnect.Sources.Focas2.Tests

# End-to-end (requires Mosquitto on localhost:1883)
dotnet test tests/ElpisEdgeConnect.Integration.Tests \
  --filter "FullyQualifiedName~Focas2ToMqttEndToEndTests"
```

## Layout

```
src/ElpisEdgeConnect.Sources.Focas2/
├── Focas2SourceAdapter.cs      ← ISourceAdapter implementation (entry point)
├── Focas2SourceConfiguration.cs ← Typed config + FromSourceInstance JSON parser
├── Focas2ConnectionManager.cs  ← Handle allocation + backoff
├── Focas2Thread.cs             ← Dedicated thread for handle affinity
├── Focas2Interop.cs            ← P/Invoke declarations + cross-platform resolver
├── Focas2NativeApi.cs          ← Production IFocas2Api implementation
├── IFocas2Api.cs               ← Seam for FakeFocas2Api in unit tests
├── Focas2TagMap.cs             ← Canonical tag-name constants
├── Focas2Errors.cs             ← Error-code catalog (FOCAS2.*)
├── Focas2FatalException.cs     ← Typed exception wrapping EW_SOCKET / EW_HANDLE
└── Collectors/                 ← Per-topic data collection logic
    ├── StatusCollector.cs
    ├── ProgramCollector.cs
    ├── AxisCollector.cs
    ├── SpindleCollector.cs
    ├── AlarmCollector.cs
    ├── ProductionCollector.cs
    ├── ToolCollector.cs
    └── MtLinkiCollector.cs
```

## Runtime dependency

The adapter P/Invokes the Fanuc FOCAS2 library. It is **not** open
source. See [`docs/adapter-sdk/focas2-adapter.md#2-prerequisites-the-fanuc-native-library`](../../docs/adapter-sdk/focas2-adapter.md#2-prerequisites-the-fanuc-native-library)
for deployment.

## Adding a new collector

1. Create `Collectors/FooCollector.cs` with a `Collect(handle, factory, points, now, now)` entry point.
2. Add tag constants to `Focas2TagMap.cs`.
3. In `Focas2SourceAdapter.InitializeAsync`, instantiate the new collector.
4. In `Focas2SourceAdapter.CollectAll`, gate the call on `HasAnyDataPoint("Foo/")` or similar.
5. Add a `FooCollectorTests.cs` in the test project.
