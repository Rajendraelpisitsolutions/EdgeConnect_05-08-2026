// ============================================================================
// File: Options/ManagementOptions.cs
// Purpose: Configuration for the Connectivity Studio management UI
//          + API host. Locked v1 defaults:
//             * bindAddress = 127.0.0.1 (localhost-only)
//             * port        = 5080
//             * auth.mode   = None  (safe because nobody off-host
//                                    can reach localhost)
//
//          Exposing the UI on the network REQUIRES the operator to
//          set bindAddress != 127.0.0.1 explicitly. Combining
//          bindAddress=0.0.0.0 (or any non-loopback) with auth=None
//          is permitted but produces a LOUD startup warning + a
//          warning log every 60s + a Prometheus gauge so observability
//          surfaces the unsafe combination.
//
//          Rationale: KepServer / Matrikon / similar incumbents
//          default to network-exposed admin UIs. We default safer
//          because the kind of operator who hasn't yet configured
//          auth shouldn't accidentally publish their admin surface
//          to the OT VLAN — but legitimate network-exposed setups
//          (lab benches, demo machines) shouldn't be blocked, just
//          warned.
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1a
//            docs/ops-runbook.md § 9 (Connectivity Studio access)
// ============================================================================

using System;

namespace ElpisEdgeConnect.Management.Options;

/// <summary>
/// Top-level Connectivity Studio host options. Bound from the
/// <c>management</c> block in <c>gateway.json</c> or via env var
/// overrides (<c>EDGECONNECT_MANAGEMENT_BIND</c>,
/// <c>EDGECONNECT_MANAGEMENT_PORT</c>).
/// </summary>
public sealed record ManagementOptions
{
    /// <summary>
    /// Network interface the Studio binds to. Default
    /// <c>127.0.0.1</c> — reachable only from the host itself.
    /// Use <c>0.0.0.0</c> (or a specific NIC IP) to expose on the
    /// network; auth SHOULD be set to <see cref="ManagementAuthMode.Basic"/>
    /// in that case.
    /// </summary>
    public string BindAddress { get; init; } = "127.0.0.1";

    /// <summary>
    /// TCP port the Studio listens on. Default <c>5080</c> — chosen
    /// to avoid conflicts with common defaults (ASP.NET dev 5000,
    /// KepServer 8080, OPC UA 4840, MQTT 1883, S7 102, Modbus 502).
    /// </summary>
    public int Port { get; init; } = 5080;

    /// <summary>
    /// Authentication configuration. Default
    /// <see cref="ManagementAuthMode.None"/> — safe in combination
    /// with the default localhost binding; unsafe (but permitted
    /// with a loud warning) when paired with a network binding.
    /// </summary>
    public ManagementAuthOptions Auth { get; init; } = new();

    /// <summary>
    /// True when the configured bind address is a loopback (i.e.
    /// 127.0.0.1 or ::1). Used by the startup logger to decide
    /// whether to emit the "unsafe exposure" warning.
    /// </summary>
    public bool IsLoopbackBinding =>
        string.Equals(BindAddress, "127.0.0.1", StringComparison.Ordinal)
        || string.Equals(BindAddress, "::1", StringComparison.Ordinal)
        || string.Equals(BindAddress, "localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the current config exposes the Studio on the network
    /// without authentication — the combination that warrants a
    /// running WARNING + observability metric.
    /// </summary>
    public bool IsNetworkExposedWithoutAuth =>
        !IsLoopbackBinding && Auth.Mode == ManagementAuthMode.None;

    /// <summary>
    /// True when the Studio is serving TLS (a certificate was supplied via
    /// the cert env vars). Drives the scheme in <see cref="BaseUrl"/> so the
    /// co-hosted components' HttpClient and the startup banner use the
    /// matching <c>https</c> scheme instead of plain <c>http</c>.
    /// </summary>
    public bool UseHttps { get; init; }

    /// <summary>
    /// Full base URL (e.g. <c>http://127.0.0.1:5080</c> or
    /// <c>https://127.0.0.1:443</c>) suitable for the co-hosted HttpClient,
    /// log messages and the runbook reference. Scheme follows
    /// <see cref="UseHttps"/>.
    /// </summary>
    public string BaseUrl => $"{(UseHttps ? "https" : "http")}://{BindAddress}:{Port}";

    /// <summary>
    /// True when the FOCAS2 demo mode is active for the current process
    /// (M.2b.3.1). Populated from <c>Focas2DemoModeOptions.IsEnabled</c>
    /// at composition time; any operator-supplied value is overwritten
    /// (Locked H: env var is the ONLY activation path).
    /// </summary>
    public bool Focas2FakeMode { get; init; }

    /// <summary>
    /// True when Siemens S7 demo mode is active for the current process
    /// (M.2b.2 follow-up). Populated from <c>S7DemoModeOptions.IsEnabled</c>
    /// at composition time; any operator-supplied value is overwritten
    /// (env var is the ONLY activation path).
    /// </summary>
    public bool S7FakeMode { get; init; }

    /// <summary>
    /// Destination the Studio's "Buy License" button opens. Overridable via
    /// the <c>EDGECONNECT_BUY_LICENSE_URL</c> env var at composition time.
    /// </summary>
    public string BuyLicenseUrl { get; init; } = "https://elpisitsolutions.com/edgeconnect";

    /// <summary>
    /// Elpis sales email shown in the Buy License dialog and used as the
    /// enquiry recipient. Override via <c>EDGECONNECT_SALES_EMAIL</c>.
    /// </summary>
    public string SalesEmail { get; init; } = "Sony.Mellam@ElpisItSolutions.com";

    /// <summary>
    /// Elpis sales / contact phone number shown in the Buy License dialog.
    /// Placeholder — set the real number via <c>EDGECONNECT_SALES_PHONE</c>.
    /// </summary>
    public string SalesPhone { get; init; } = "+91 80 4000 0000";

    /// <summary>
    /// Elpis company website shown in the Buy License dialog. Override via
    /// <c>EDGECONNECT_COMPANY_WEBSITE</c>.
    /// </summary>
    public string CompanyWebsite { get; init; } = "https://elpisitsolutions.com";
}

/// <summary>
/// Authentication options. Empty when <see cref="Mode"/> is
/// <see cref="ManagementAuthMode.None"/>; populated when Basic auth
/// is enabled.
/// </summary>
public sealed record ManagementAuthOptions
{
    /// <summary>
    /// Authentication scheme. Default
    /// <see cref="ManagementAuthMode.None"/>. v1 supports only
    /// None and Basic; OIDC / SSO is a future enhancement.
    /// </summary>
    public ManagementAuthMode Mode { get; init; } = ManagementAuthMode.None;

    /// <summary>
    /// Valid users when <see cref="Mode"/> is
    /// <see cref="ManagementAuthMode.Basic"/>. Plain-text password
    /// storage at MVP, with the same ACL-the-config-dir guidance the
    /// OPC UA Server credential list (Milestone K) carries.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<ManagementUser> Users { get; init; } =
        System.Array.Empty<ManagementUser>();
}

/// <summary>v1 management auth modes.</summary>
public enum ManagementAuthMode
{
    /// <summary>No authentication. Safe iff the binding is loopback.</summary>
    None = 0,

    /// <summary>HTTP Basic authentication against the configured user list.</summary>
    Basic = 1,
}

/// <summary>
/// One operator-configured management user. Plain-text password at
/// MVP; M' will introduce hashed storage and admin-API rotation.
/// </summary>
public sealed record ManagementUser
{
    /// <summary>Username (case-sensitive).</summary>
    public required string Username { get; init; }

    /// <summary>Plain-text password. Operators MUST restrict <c>gateway.json</c> permissions.</summary>
    public required string Password { get; init; }
}
