// ============================================================================
// File: Configuration/MqttSettings.cs
// Purpose: Defines all configuration for connecting to an MQTT broker and
//          controlling how messages are published (batching, QoS, TLS, etc.).
//
// This class is bound from the "MqttSettings" section of appsettings.json
// and injected into MqttPublisherService via IOptions<MqttSettings>.
//
// SECURITY OVERVIEW:
//   - UseTls:                  Encrypts all broker communication
//   - CaCertificatePath:       Verifies broker's TLS certificate
//   - ClientCertificatePath:   Mutual TLS (mTLS) — broker verifies this client
//   - Password:                Supports encrypted values via SecretProvider
//
// MQTT TOPIC STRUCTURE:
//   {TopicPrefix}/{machineId}/data          → Machine telemetry payloads
//   {TopicPrefix}/$bridge/status            → "online"/"offline" (retained)
//   {TopicPrefix}/$bridge/diagnostics       → Bridge health metrics (retained)
//
//   Example: factory/cnc/CNC-001/data
//   Subscribers can use wildcards:
//     factory/cnc/+/data   → All machines' data
//     factory/cnc/#        → Everything (data + bridge status)
// ============================================================================

using System.ComponentModel.DataAnnotations;

namespace ElpisEdgeConnect.Configuration;

/// <summary>
/// MQTT broker connection and publishing settings.
/// Supports TLS with optional client certificates for mutual authentication (mTLS).
/// 
/// BATCHING STRATEGY:
///   Messages are published in batches for maximum throughput. The publisher
///   waits until either:
///     a) PublishBatchSize messages have accumulated, OR
///     b) PublishBatchIntervalMs has elapsed since the first message in the batch
///   whichever comes first. This ensures low-latency publishing even when
///   message rates are low, while still benefiting from batching at high rates.
/// </summary>
public sealed class MqttSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// Referenced in Program.cs: builder.Services.Configure&lt;MqttSettings&gt;(config.GetSection("MqttSettings"))
    /// </summary>
    public const string SectionName = "MqttSettings";

    /// <summary>
    /// MQTT broker hostname or IP address.
    /// 
    /// Examples:
    ///   - "localhost"                    → Local development broker
    ///   - "mqtt-broker"                  → Docker Compose service name
    ///   - "broker.hivemq.com"            → Cloud-hosted broker
    ///   - "192.168.1.200"                → Factory floor broker
    /// 
    /// For Docker Compose deployments, use the service name (e.g., "mqtt-broker")
    /// which resolves to the container's IP via Docker's internal DNS.
    /// </summary>
    [Required]
    public string BrokerHost { get; set; } = "localhost";

    /// <summary>
    /// MQTT broker TCP port.
    /// 
    /// Common ports:
    ///   - 1883:  Standard MQTT (unencrypted TCP) — NEVER use in production
    ///   - 8883:  MQTT over TLS (encrypted TCP) — REQUIRED for production
    ///   - 9001:  WebSocket (unencrypted) — for browser-based clients
    ///   - 443:   WebSocket over TLS (wss://) — production WebSocket
    ///   - 8084:  WebSocket over TLS (alternate port)
    /// </summary>
    [Range(1, 65535)]
    public int BrokerPort { get; set; } = 8883;

    /// <summary>
    /// MQTT protocol version to use when connecting to the broker.
    /// 
    /// Values:
    ///   "V310"  — MQTT 3.1   (legacy, rarely used)
    ///   "V311"  — MQTT 3.1.1 (widely supported, most brokers)
    ///   "V500"  — MQTT 5.0   (newest, requires broker support)
    /// 
    /// Use "V311" for maximum compatibility with MQTT 3.x brokers.
    /// MQTTnet 4.x defaults to V500 if not specified — which will FAIL
    /// against brokers that only support 3.x.
    /// </summary>
    public string ProtocolVersion { get; set; } = "V311";

    /// <summary>
    /// Transport type for the MQTT connection.
    /// 
    /// Values:
    ///   "Tcp"       — Standard TCP socket (default for server-to-server)
    ///   "WebSocket" — WebSocket transport (ws:// or wss:// with TLS)
    /// 
    /// Use "WebSocket" when:
    ///   - Broker only exposes WebSocket endpoints
    ///   - Firewall/proxy only allows HTTP/WS traffic
    ///   - Broker is behind a reverse proxy (nginx, HAProxy)
    /// 
    /// WebSocket URL is constructed as:
    ///   ws(s)://{BrokerHost}:{BrokerPort}/{WebSocketPath}
    /// </summary>
    public string Transport { get; set; } = "Tcp";

    /// <summary>
    /// WebSocket path appended to the broker URL when Transport = "WebSocket".
    /// 
    /// Common paths:
    ///   "/mqtt"     — Mosquitto, EMQX, HiveMQ default
    ///   "/ws"       — Some brokers use this
    ///   "/"         — Root path
    /// 
    /// The full WebSocket URI becomes:
    ///   ws://{BrokerHost}:{BrokerPort}/{WebSocketPath}   (UseTls=false)
    ///   wss://{BrokerHost}:{BrokerPort}/{WebSocketPath}  (UseTls=true)
    /// </summary>
    public string WebSocketPath { get; set; } = "/mqtt";

    /// <summary>
    /// Enable TLS encryption for the MQTT connection.
    /// 
    /// When true:
    ///   - Connection uses TLS 1.2 or 1.3 (configured in MqttPublisherService)
    ///   - CaCertificatePath is used to verify the broker's certificate
    ///   - ClientCertificatePath enables mutual TLS if provided
    /// 
    /// STRONGLY RECOMMENDED for production. Set to false only for local
    /// development with an unencrypted broker on localhost.
    /// </summary>
    public bool UseTls { get; set; } = true;

    /// <summary>
    /// Unique MQTT client identifier for this bridge instance.
    /// 
    /// MUST be unique across all clients connecting to the same broker.
    /// If two clients connect with the same ID, the broker will disconnect
    /// the first one (MQTT spec behavior).
    /// 
    /// Defaults to "edgeconnect-{hostname}" for automatic uniqueness.
    /// For multi-instance deployments, use explicit names:
    ///   "edgeconnect-prod-01", "edgeconnect-prod-02", etc.
    /// 
    /// The client ID also appears in broker logs, making it useful for
    /// debugging connection issues.
    /// </summary>
    [Required]
    public string ClientId { get; set; } = $"edgeconnect-{Environment.MachineName}";

    /// <summary>
    /// MQTT broker authentication username.
    /// 
    /// Set to empty string for brokers that allow anonymous connections
    /// (not recommended for production). Most production brokers require
    /// username + password authentication.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// MQTT broker authentication password.
    /// 
    /// Supports the same secret resolution as machine passwords:
    ///   - "env:MQTT_PASSWORD"         → Reads from environment variable
    ///   - "enc:IV:CIPHERTEXT"         → AES-256 decrypted at runtime
    ///   - "plaintext"                 → Used as-is (not recommended)
    /// 
    /// The password is resolved by SecretProvider before connecting.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// MQTT topic prefix prepended to all published topics.
    /// 
    /// Resulting topic structure:
    ///   {TopicPrefix}/{machineId}/data          → Machine telemetry
    ///   {TopicPrefix}/$bridge/status            → Bridge status (retained)
    ///   {TopicPrefix}/$bridge/diagnostics       → Bridge metrics (retained)
    /// 
    /// Examples:
    ///   "factory/cnc"     → factory/cnc/CNC-001/data
    ///   "plant-a/floor-2" → plant-a/floor-2/CNC-001/data
    /// 
    /// Use hierarchical naming for multi-factory or multi-floor deployments.
    /// MQTT subscribers can use wildcards to subscribe to specific levels.
    /// </summary>
    public string TopicPrefix { get; set; } = "factory/cnc";

    /// <summary>
    /// Default MQTT Quality of Service level for published messages.
    /// 
    /// QoS Levels:
    ///   0 (AtMostOnce):  Fire-and-forget. Fastest, but messages may be lost.
    ///                    Use for high-frequency, non-critical data (e.g., positions).
    ///   
    ///   1 (AtLeastOnce): Guaranteed delivery, but may receive duplicates.
    ///                    RECOMMENDED for most CNC data — best balance of
    ///                    reliability and performance.
    ///   
    ///   2 (ExactlyOnce): Guaranteed exactly-once delivery. Slowest (4-way handshake).
    ///                    Use only for critical data like alarm events or parts counts.
    /// 
    /// Each individual MqttPayload can override this with its own QoS value.
    /// </summary>
    [Range(0, 2)]
    public int QualityOfServiceLevel { get; set; } = 1;

    /// <summary>
    /// Whether to start with a clean MQTT session on each connection.
    /// 
    /// When false (default, RECOMMENDED):
    ///   - The broker remembers subscriptions and queued messages for this client
    ///   - If the bridge disconnects and reconnects, it receives messages that
    ///     were published while it was offline (for subscribed topics)
    ///   - This is called a "persistent session"
    /// 
    /// When true:
    ///   - Each connection starts fresh — no queued messages, no saved subscriptions
    ///   - Use only if the bridge doesn't subscribe to any topics
    /// 
    /// Since this bridge primarily PUBLISHES (not subscribes), persistent sessions
    /// mainly help the broker track QoS 1/2 acknowledgments across reconnections.
    /// </summary>
    public bool CleanSession { get; set; } = false;

    /// <summary>
    /// MQTT keep-alive interval in seconds.
    /// 
    /// The client sends a PINGREQ packet to the broker at this interval
    /// to keep the connection alive and detect broken connections.
    /// 
    /// If the broker doesn't receive a PINGREQ within 1.5× this interval,
    /// it considers the client disconnected and publishes the Last Will message.
    /// 
    /// TUNING:
    ///   - 15-30s:  Good for reliable networks (less overhead)
    ///   - 5-10s:   Faster disconnection detection (more overhead)
    ///   - Never set above 60s for production environments
    /// </summary>
    [Range(5, 300)]
    public int KeepAliveSeconds { get; set; } = 30;

    /// <summary>
    /// Delay in milliseconds before attempting to reconnect after disconnection.
    /// 
    /// The actual reconnect logic uses exponential backoff:
    ///   Attempt 1: ReconnectDelayMs (e.g., 5000ms)
    ///   Attempt 2: ReconnectDelayMs × 1.5 (7500ms)
    ///   Attempt 3: ReconnectDelayMs × 2.25 (11250ms)
    ///   ...capped at 60,000ms (1 minute)
    /// 
    /// This prevents hammering the broker with reconnection attempts
    /// when it's temporarily unavailable.
    /// </summary>
    public int ReconnectDelayMs { get; set; } = 5000;

    /// <summary>
    /// Maximum number of messages the MQTT client queues internally
    /// when waiting for QoS 1/2 acknowledgments from the broker.
    /// 
    /// If the broker is slow to acknowledge, messages pile up here.
    /// Once this limit is reached, new publishes may block or fail.
    /// 
    /// This is separate from the Channel&lt;MqttPayload&gt; capacity
    /// (which is the application-level buffer controlled by ChannelCapacity).
    /// </summary>
    public int MaxPendingMessages { get; set; } = 5000;

    /// <summary>
    /// Number of MQTT messages to publish per batch.
    /// 
    /// The MqttPublisherService reads messages from the channel in batches
    /// of this size. Larger batches = higher throughput but slightly higher
    /// latency for individual messages.
    /// 
    /// TUNING:
    ///   - 50-100:  Good for ≤100 machines at 1Hz (50-100 msgs/sec)
    ///   - 200-500: Better for 200+ machines or sub-second polling
    ///   - 1000:    Maximum — only for very high throughput scenarios
    /// 
    /// The batch is flushed either when it reaches this size OR when
    /// PublishBatchIntervalMs expires, whichever comes first.
    /// </summary>
    [Range(1, 1000)]
    public int PublishBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum time in milliseconds to wait before flushing a partial batch.
    /// 
    /// If fewer than PublishBatchSize messages arrive within this window,
    /// the partial batch is flushed anyway. This ensures messages aren't
    /// delayed too long when the arrival rate is low.
    /// 
    /// TUNING:
    ///   - 100-250ms:  Low latency, good for real-time dashboards
    ///   - 500-1000ms: Higher throughput, acceptable for historical logging
    ///   - Never exceed 5000ms — stale data loses value in CNC monitoring
    /// </summary>
    [Range(50, 5000)]
    public int PublishBatchIntervalMs { get; set; } = 250;

    /// <summary>
    /// Publish mode: how machine data is structured into MQTT messages.
    ///
    /// Values:
    ///   "Bundled"  — (Default) All tags in ONE JSON message per machine per poll.
    ///                Topic: {TopicPrefix}/{machineId}/data
    ///                Payload: { "DeviceId": "...", "Data": ["tag,value,ts,type", ...] }
    ///
    ///   "PerTag"   — Each tag published as a SEPARATE MQTT message with raw value.
    ///                Topic: {EremosTopicPrefix}/{machineId}/{tagName}
    ///                Payload: raw value string (e.g., "MEM", "142", "45.2")
    ///                Designed for EREMOS platform integration where each topic
    ///                maps 1:1 to a tag in the routing table.
    ///
    /// NOTE: Both modes can operate simultaneously if needed (future enhancement).
    /// For now, set to "PerTag" when integrating with EREMOS, "Bundled" for legacy.
    /// </summary>
    public string PublishMode { get; set; } = "Bundled";

    /// <summary>
    /// When true, the MQTT publisher logs payloads to console/file instead of
    /// actually publishing to the broker. Use this for testing FOCAS2 data
    /// collection without sending data to EREMOS or any MQTT consumer.
    ///
    /// The service still connects to the broker (for health reporting), but
    /// all machine data payloads are logged and discarded.
    ///
    /// Set to false for production.
    /// </summary>
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// Topic prefix used when PublishMode = "PerTag".
    ///
    /// Each tag is published to: {EremosTopicPrefix}/{machineId}/{tagName}
    ///
    /// Examples:
    ///   "eremos/demo/cnc"   → eremos/demo/cnc/AMS1CNC/Mode_path1_CNC
    ///   "eremos/factory-1"  → eremos/factory-1/AMS1CNC/SpindleTemp_0_path1_CNC
    ///
    /// EREMOS SharedMqttClient subscribes to: {EremosTopicPrefix}/#
    /// and routes each topic to the corresponding tag via the routing table.
    ///
    /// Only used when PublishMode = "PerTag". Ignored in "Bundled" mode.
    /// </summary>
    public string EremosTopicPrefix { get; set; } = "eremos/demo/cnc";

    /// <summary>
    /// File path to the CA (Certificate Authority) certificate for verifying
    /// the MQTT broker's TLS certificate.
    /// 
    /// If your broker uses a certificate signed by a public CA (e.g., Let's Encrypt),
    /// you can leave this empty — the system's trusted CA store handles it.
    /// 
    /// For self-signed broker certificates, provide the CA certificate here:
    ///   - PEM format (.crt, .pem): Most common
    ///   - Path example: "/app/certs/ca.crt" (Docker) or "C:\certs\ca.crt" (Windows)
    /// </summary>
    public string CaCertificatePath { get; set; } = string.Empty;

    /// <summary>
    /// File path to the client certificate for mutual TLS (mTLS) authentication.
    /// 
    /// When provided, the broker verifies this client's identity in addition
    /// to the client verifying the broker. This is the strongest form of
    /// MQTT authentication.
    /// 
    /// Format: PKCS#12 / PFX file (.pfx, .p12) containing both the
    /// client certificate and private key.
    /// 
    /// GENERATING A CLIENT CERT (example with OpenSSL):
    ///   openssl pkcs12 -export -out client.pfx \
    ///     -inkey client.key -in client.crt -certfile ca.crt
    /// </summary>
    public string ClientCertificatePath { get; set; } = string.Empty;

    /// <summary>
    /// Password for the client certificate PFX file.
    /// 
    /// Required if ClientCertificatePath points to a password-protected PFX.
    /// Supports encrypted values via SecretProvider (same as machine passwords).
    /// 
    /// Example: "env:MQTT_CLIENT_CERT_PASSWORD"
    /// </summary>
    public string ClientCertificatePassword { get; set; } = string.Empty;
}
