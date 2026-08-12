// ============================================================================
// File: Configuration/AppSettings.cs
// Purpose: Defines top-level application settings that control concurrency,
//          back-pressure, polling defaults, and health check behavior.
// 
// These settings are bound from the "AppSettings" section of appsettings.json
// and can be changed at runtime via hot-reload (IOptionsMonitor<T>).
//
// Example JSON:
//   "AppSettings": {
//       "MaxParallelPolls": 50,
//       "ChannelCapacity": 10000,
//       "DefaultPollIntervalMs": 1000,
//       "EnableHealthChecks": true,
//       "HealthCheckPort": 8080
//   }
// ============================================================================

namespace ElpisEdgeConnect.Configuration;

/// <summary>
/// Top-level application settings controlling concurrency and back-pressure.
/// 
/// SCALABILITY NOTES:
/// - MaxParallelPolls controls how many CNC machines are queried simultaneously.
///   Set this based on your network bandwidth and CPU cores.
///   Rule of thumb: 50 for ~100 machines, 80-100 for 200+ machines.
///   
/// - ChannelCapacity controls the bounded channel buffer between pollers and
///   the MQTT publisher. If the channel fills up (MQTT is slow), the oldest
///   messages are dropped to prevent memory exhaustion.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// The configuration section name in appsettings.json.
    /// Used in Program.cs when binding:
    ///   builder.Services.Configure&lt;AppSettings&gt;(config.GetSection("AppSettings"))
    /// </summary>
    public const string SectionName = "AppSettings";

    /// <summary>
    /// Maximum number of machines polled simultaneously.
    /// 
    /// This value initializes a SemaphoreSlim in MachineManagerService,
    /// acting as a concurrency gate. When starting pollers, each must
    /// acquire the semaphore before beginning — preventing thread pool
    /// starvation when hundreds of machines try to connect at once.
    /// 
    /// TUNING GUIDE:
    ///   - 10-30:  Conservative — limited network bandwidth
    ///   - 50:     Default — suitable for most factory networks with ~100 machines
    ///   - 80-100: Aggressive — high-bandwidth networks with 200+ machines
    ///   - Never exceed the ThreadPool minimum (Environment.ProcessorCount × N)
    /// </summary>
    public int MaxParallelPolls { get; set; } = 50;

    /// <summary>
    /// Bounded channel capacity — max MQTT payloads queued between
    /// the polling producers and the MQTT publishing consumer.
    /// 
    /// HOW BACK-PRESSURE WORKS:
    /// 1. Each machine poller writes collected data to a Channel&lt;MqttPayload&gt;.
    /// 2. MqttPublisherService reads from this channel and publishes to the broker.
    /// 3. If the publisher is slower than pollers (e.g., broker is down), the
    ///    channel fills up to this capacity.
    /// 4. Once full, the OLDEST messages are dropped (BoundedChannelFullMode.DropOldest)
    ///    ensuring we always have the most recent machine data.
    /// 
    /// SIZING GUIDE:
    ///   Formula: MachineCount × (1000 / AveragePollIntervalMs) × BufferSeconds
    ///   Example: 100 machines × 1 poll/sec × 10 sec buffer = 1,000
    ///   Default of 10,000 gives ~100 seconds of buffer for 100 machines at 1Hz.
    /// </summary>
    public int ChannelCapacity { get; set; } = 10_000;

    /// <summary>
    /// Default poll interval in milliseconds — used when a machine's config
    /// does not specify its own PollIntervalMs value.
    /// 
    /// 1000ms (1 second) is a good balance between real-time updates and
    /// network load. Fanuc FOCAS web services typically handle 2-10
    /// requests per second per machine without issues.
    /// 
    /// WARNING: Setting below 200ms may overwhelm the CNC controller's
    /// web server and cause connection timeouts or dropped requests.
    /// </summary>
    public int DefaultPollIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Whether to expose an HTTP health check endpoint (e.g., /health).
    /// 
    /// When enabled, the service listens on HealthCheckPort and responds
    /// to health check requests from Kubernetes, Docker, or load balancers.
    /// The health check verifies:
    ///   1. Service process is running (liveness)
    ///   2. MQTT broker connection is active (readiness)
    /// 
    /// Disable if external health monitoring isn't needed or you're behind
    /// a firewall that shouldn't expose any ports.
    /// </summary>
    public bool EnableHealthChecks { get; set; } = true;

    /// <summary>
    /// TCP port for the health check HTTP listener.
    /// 
    /// Separate from the main service (which has no HTTP endpoints);
    /// used only for health probes. Common choices:
    ///   - 8080:   Default, no conflicts with standard services
    ///   - 13000+: High ports to avoid conflicts in shared environments
    /// 
    /// In Docker/Kubernetes, map this port in your deployment config:
    ///   ports: ["8080:8080"]
    ///   livenessProbe: httpGet: { path: /health, port: 8080 }
    /// </summary>
    public int HealthCheckPort { get; set; } = 8080;
}
