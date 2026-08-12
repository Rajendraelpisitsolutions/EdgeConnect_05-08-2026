# 05 — MQTT integration tests

The runtime publishes per-tag canonical data to MQTT (`eremos/{gatewayId}/{deviceClass}/{sourceId}/{tagName}`). The MQTT sink integration tests stand up a real publisher against a local broker — they will not run against a mocked broker by design (see `docs/decisions/` for the lock).

## Install Mosquitto

**Windows:**

1. Download from [mosquitto.org/download/](https://mosquitto.org/download/).
2. Install with the default options.
3. Confirm:

   ```pwsh
   mosquitto -v
   ```

4. Make sure it's running on `localhost:1883` with anonymous access. Default config does that on first install; if you've customized, edit `C:\Program Files\mosquitto\mosquitto.conf` and confirm:

   ```
   listener 1883 127.0.0.1
   allow_anonymous true
   ```

5. As a service: `net start mosquitto` (Windows) — runs at boot.

**Linux:**

```bash
sudo apt install mosquitto
sudo systemctl enable --now mosquitto
```

## Verify the broker

```pwsh
mosquitto_sub -h localhost -t '#' -v
```

That subscribes to all topics. In another terminal:

```pwsh
mosquitto_pub -h localhost -t test/hello -m 'world'
```

The subscriber should print `test/hello world`. If not, the broker isn't running on `localhost:1883`.

## Run the MQTT tests

```pwsh
dotnet test tests\ElpisEdgeConnect.Sinks.Mqtt.Tests\ --nologo
```

These tests are skipped if `localhost:1883` is unreachable. You'll see them as `Skipped` rather than failing — but you want them to actually run, so confirm the broker is up first.

The integration test project is `tests/ElpisEdgeConnect.Integration.Tests/` — it covers cross-layer flows (source → pipeline → MQTT sink, store-and-forward replay, etc.).

```pwsh
dotnet test tests\ElpisEdgeConnect.Integration.Tests\ --nologo
```

Expect a slower run (~30-60 s) — these tests do real I/O.

## EREMOS V2 contract

EdgeConnect publishes on the MQTT topic shape EREMOS V2 expects:

```
eremos/{gatewayId}/{deviceClass}/{sourceId}/{tagName}
```

The full contract — payload shape, QoS expectations, retain rules, reconnect behavior — lives in `C:\dev\shared-knowledge\contracts\eremos-per-tag-mqtt.md`. Read it before any sink-layer work that touches the topic structure or payload encoding.

## Done?

Continue to [06-codebase-tour.md](06-codebase-tour.md).
