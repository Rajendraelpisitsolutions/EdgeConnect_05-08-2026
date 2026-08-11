# ElpisEdgeConnect.Sources.MTConnect

MTConnect source adapter — polls the Agent's `/current` endpoint on
HTTP, parses the XML response, and emits canonical data points.

Implements `Core.Adapters.ISourceAdapter`. No native library required;
works on Windows and Linux identically.

## Quick links

- **Full deployment & configuration guide:** [`docs/adapter-sdk/mtconnect-adapter.md`](../../docs/adapter-sdk/mtconnect-adapter.md)
- **Contract this adapter implements:** [`docs/adapter-sdk/source-adapter-contract.md`](../../docs/adapter-sdk/source-adapter-contract.md)
- **Unit tests (38):** [`tests/ElpisEdgeConnect.Sources.MTConnect.Tests/`](../../tests/ElpisEdgeConnect.Sources.MTConnect.Tests/)
- **End-to-end integration test:** [`tests/ElpisEdgeConnect.Integration.Tests/MTConnectToMqttEndToEndTests.cs`](../../tests/ElpisEdgeConnect.Integration.Tests/MTConnectToMqttEndToEndTests.cs)

## Build

```bash
dotnet build src/ElpisEdgeConnect.Sources.MTConnect/ElpisEdgeConnect.Sources.MTConnect.csproj
```

## Test

```bash
# Unit tests — no Agent, no broker
dotnet test tests/ElpisEdgeConnect.Sources.MTConnect.Tests

# End-to-end (requires Mosquitto on localhost:1883)
dotnet test tests/ElpisEdgeConnect.Integration.Tests \
  --filter "FullyQualifiedName~MTConnectToMqttEndToEndTests"
```

## Layout

```
src/ElpisEdgeConnect.Sources.MTConnect/
├── MTConnectSourceAdapter.cs         ← ISourceAdapter implementation
├── MTConnectSourceConfiguration.cs   ← Typed config + FromSourceInstance JSON parser
├── MTConnectStreamParser.cs          ← Pure XML-to-canonical-points parser
├── IMTConnectClient.cs               ← HTTP seam for FakeMTConnectClient
├── MTConnectHttpClient.cs            ← Production HttpClient-backed impl
├── MTConnectTagMap.cs                ← Canonical tag-name constants
└── MTConnectErrors.cs                ← Error-code catalogue (MTCONNECT.*)
```

## Adding support for a new Agent quirk

If a vendor's Agent emits an unusual element name the parser doesn't
recognize (e.g. `<MyVendorSpindle>` instead of `<SpindleSpeed>`):

1. Capture a representative `/current` response under `tests/ElpisEdgeConnect.Sources.MTConnect.Tests/TestData/`.
2. Add a test in `MTConnectStreamParserTests` that loads the fixture and
   asserts the expected tag emission.
3. Extend `MTConnectStreamParser.ParseCurrent` — usually this means
   adding an alternate key to the relevant `TryGetDouble` / `TryGetLong`
   lookup-key array (e.g. `SpindleSpeedKeys`).
4. Run the new test + the existing suite.
