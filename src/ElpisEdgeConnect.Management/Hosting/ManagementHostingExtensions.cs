// ============================================================================
// File: Hosting/ManagementHostingExtensions.cs
// Purpose: One-stop wire-up for the Connectivity Studio. The EdgeConnect
//          Host calls AddConnectivityStudio + UseConnectivityStudio
//          right after AddElpisEdgeConnectHost; from there the management
//          UI + API stand up alongside the runtime.
//
//          Behavior gates:
//             * License module `connectivity-studio` checked at AddX
//               time. If missing AND a license is loaded, every
//               wire-up here becomes a no-op (just like the source
//               adapter G.7 gate).
//             * BindAddress + port come from ManagementOptions; env
//               var overrides EDGECONNECT_MANAGEMENT_BIND /
//               EDGECONNECT_MANAGEMENT_PORT win when set.
//             * Basic auth only registered when explicitly enabled.
//             * Startup announcer + Prometheus insecure-exposure
//               gauge always wired.
//
//          The Razor↔Core isolation rule is NOT enforced here at
//          runtime — it's a build-time invariant pinned by an
//          assembly-load test (see ManagementProjectIsolationTests).
// Reference: docs/PHASE4_EXECUTION_PLAN.md Milestone M.1a
//            docs/licensing/module-catalog.md (connectivity-studio)
// ============================================================================

using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Management.Api;
using ElpisEdgeConnect.Management.Api.BulkSourceMerge;
using ElpisEdgeConnect.Management.Hosting;
using ElpisEdgeConnect.Management.Options;
using ElpisEdgeConnect.Management.Security;
using ElpisEdgeConnect.Sources.Focas2;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using MudBlazor.Services;

namespace ElpisEdgeConnect.Management;

/// <summary>
/// Public host-side wire-up surface for Connectivity Studio.
/// </summary>
public static class ManagementHostingExtensions
{
    /// <summary>License module key gating registration. Mirrors <c>LicenseModuleKeys.ConnectivityStudio</c>.</summary>
    public const string LicenseModuleKey = "connectivity-studio";

    /// <summary>
    /// Studio-owned snackbar position class — centres toasts on both axes.
    /// Defined in <c>wwwroot/css/site.css</c>; MudBlazor ships no both-axes
    /// centred position of its own, so this is not a vendor class name and
    /// will not collide with one on upgrade.
    /// </summary>
    public const string SnackbarCentreMiddleClass = "mud-snackbar-location-centre-middle";

    /// <summary>
    /// Register Connectivity Studio services. Idempotent and a no-op
    /// when the <c>connectivity-studio</c> license module is missing
    /// (per Locked Decision #10 — disabled modules silently skip).
    /// Returns the options the caller bound, in case the host wants
    /// to log them.
    /// </summary>
    public static ManagementOptions? AddConnectivityStudio(
        this WebApplicationBuilder builder,
        ManagementOptions? options = null,
        ILicenseManager? license = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // License gate. Permissive when no license is loaded (dev /
        // sim / first-run scenario) — matches the G.7 source-adapter
        // pattern.
        if (license is { Current: not null }
            && !license.IsModuleEnabled(LicenseModuleKey))
        {
            Console.Error.WriteLine(
                $"[license] Connectivity Studio configured but license module " +
                $"'{LicenseModuleKey}' is not enabled. Skipping registration.");
            return null;
        }

        // Resolve options. Order of precedence:
        //   1) caller-supplied
        //   2) env vars (EDGECONNECT_MANAGEMENT_BIND / _PORT)
        //   3) defaults (127.0.0.1:5080, auth=None)
        var resolved = ApplyEnvOverrides(options ?? new ManagementOptions());

        // M.2b.3.1 — Focas2FakeMode is the SOLE responsibility of
        // Focas2DemoModeOptions (env var only). Any operator-supplied
        // value on caller-side options is overwritten unconditionally
        // (Locked H: saved configuration cannot enable demo mode).
        resolved = resolved with
        {
            Focas2FakeMode = Focas2DemoModeOptions.IsEnabled,
            S7FakeMode = ElpisEdgeConnect.Sources.S7.S7DemoModeOptions.IsEnabled,
        };

        // Optional TLS. If a PFX certificate is supplied via env vars the
        // Studio serves HTTPS; otherwise it stays plain HTTP (unchanged
        // default). This keeps the localhost-only default trivial while
        // letting an operator front the Studio with a real/self-signed cert
        // (e.g. bound to a friendly host name) without a reverse proxy.
        //   EDGECONNECT_MANAGEMENT_CERT_PFX      — path to a .pfx file
        //   EDGECONNECT_MANAGEMENT_CERT_PASSWORD — its password (optional)
        var certPfx = Environment.GetEnvironmentVariable("EDGECONNECT_MANAGEMENT_CERT_PFX");
        var certPassword = Environment.GetEnvironmentVariable("EDGECONNECT_MANAGEMENT_CERT_PASSWORD");
        X509Certificate2? tlsCertificate = null;
        if (!string.IsNullOrWhiteSpace(certPfx) && File.Exists(certPfx))
        {
            tlsCertificate = string.IsNullOrEmpty(certPassword)
                ? new X509Certificate2(certPfx)
                : new X509Certificate2(certPfx, certPassword);
        }

        // Record TLS on the resolved options BEFORE they are registered, so
        // the co-hosted components' HttpClient (BaseUrl) and the startup
        // banner use the https scheme that matches the listener.
        resolved = resolved with { UseHttps = tlsCertificate is not null };

        // Configure Kestrel to bind to the configured address+port.
        builder.WebHost.ConfigureKestrel(k =>
        {
            // Fall back to localhost loopback if the operator wrote a non-IP
            // value — safer than binding everywhere.
            var ip = IPAddress.TryParse(resolved.BindAddress, out var parsed)
                ? parsed
                : IPAddress.Loopback;
            k.Listen(ip, resolved.Port, listen =>
            {
                if (tlsCertificate is not null)
                {
                    listen.UseHttps(tlsCertificate);
                }
            });
        });

        // Force static-web-asset discovery regardless of environment.
        // Out of the box, ASP.NET Core only loads the
        // <project>.staticwebassets.runtime.json manifest in Development.
        // We need MudBlazor's _content/MudBlazor/... assets to resolve
        // when EdgeConnect runs as a Windows service / systemd unit /
        // production console — none of which set
        // ASPNETCORE_ENVIRONMENT=Development.
        builder.WebHost.UseStaticWebAssets();

        builder.Services.AddSingleton(resolved);
        builder.Services
            .AddRazorComponents()
            .AddInteractiveServerComponents()
            // Default SignalR MaximumReceiveMessageSize is 32 KB, which the
            // ImportDraftDialog's paste-textarea path easily exceeds for any
            // realistic gateway.json (~14 KB on disk → larger after Blazor
            // serialization framing). Raise to 5 MiB. Discovered during the
            // M.P2.2 phase 3 smoke procedure.
            .AddHubOptions(o => o.MaximumReceiveMessageSize = 5 * 1024 * 1024);
        // Snackbar position is set ONCE here rather than per call site. Every
        // save tail in the Studio — Wizards/WizardDraftApplier.cs (the standalone
        // Add-source / Add-destination wizards), OnboardingFlow's
        // ApplySavedEntityAsync (both Configure-source and Configure-destination
        // overlays), and the per-wizard edit-mode "…updated." saves — calls
        // ISnackbar.Add without naming a position, so one setting moves all of
        // them. Thirteen call sites each carrying their own position class is
        // thirteen chances to drift apart the first time one is edited.
        //
        // Centre rather than a corner: MudBlazor's default is
        // Defaults.Classes.Position.TopRight, which parks the "saved" confirmation
        // in the far corner of a wide panel — the one place an operator who is
        // already typing the next field is not looking. Centring is the whole
        // point of the change.
        //
        // TOP-centre and not BottomCenter, specifically. Both constants exist, but
        // `mud-snackbar-location-bottom-center` resolves to `bottom:24px`, which
        // lands *inside* the fixed 32px status footer (--footer-height in
        // site.css, StatusFooter is position:fixed;bottom:0) and directly on top of
        // .sticky-action-bar, which is `sticky; bottom:0` and carries the Save and
        // Continue buttons. That would cover live gateway health with a toast and
        // put it over the control the operator just clicked. TopCenter keeps the
        // very same `top:24px` offset the default TopRight already used, so it adds
        // no vertical collision that today's toast does not already have — it only
        // moves horizontally. It overlaps the sticky .page-heading band, but the
        // snackbar container sits at --mud-zindex-snackbar (1400) against the
        // band's z-index 5 and auto-dismisses, so it paints over static, already
        // read title text for a few seconds and never blocks it. Same reasoning
        // clears the onboarding overlay (z-index 1200).
        //
        // This is global, so error and warning toasts move with it. That is
        // intended: one notification location is easier to learn than two, and a
        // failure notice benefits from the centre at least as much as a success.
        // …and now centred on BOTH axes, not just horizontally. MudBlazor stamps
        // PositionClass straight onto #mud-snackbar-container, whose only vendor
        // rule is `position: fixed`; each of the six built-in location classes
        // supplies its own offsets, and every one of them pins an edge:
        //
        //     top-center     top:24px;    left:50%; transform:translateX(-50%)
        //     bottom-center  bottom:24px; left:50%; transform:translateX(-50%)
        //
        // So none of the six centres vertically — there was no constant to switch
        // to and the class had to be written. See .mud-snackbar-location-centre-middle
        // in wwwroot/css/site.css.
        builder.Services.AddMudServices(o =>
            o.SnackbarConfiguration.PositionClass = SnackbarCentreMiddleClass);
        builder.Services.AddRouting();
        builder.Services.AddEndpointsApiExplorer();
        // Swagger UI for the management REST API (the top-bar "Open API
        // documentation" button links to /swagger). Served in all environments
        // since the Studio binds to loopback only.
        builder.Services.AddSwaggerGen(o =>
            o.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "Elpis EdgeConnect — Management API",
                Version = "v1",
            }));

        // Diagnostics aggregators — single seam between Core's specialized
        // event ring buffers and the management API's normalized
        // DiagnosticsEventDto wire shape.
        //   * IRouteEventAggregator (M.1c.1): per-route merge.
        //   * IDiagnosticsEventAggregator (M.1c.2): system-wide merge +
        //     configuration audit projection + chain verification.
        // Both stateless; both safe as singletons.
        builder.Services.AddSingleton<Diagnostics.IRouteEventAggregator, Diagnostics.RouteEventAggregator>();
        builder.Services.AddSingleton<Diagnostics.IDiagnosticsEventAggregator, Diagnostics.DiagnosticsEventAggregator>();

        // Backup export (M.1c.3; redaction unified onto ConfigRedactionEngine
        // in M-A per ADR-0020 Amendment 1). The engine is pure / stateless.
        // BackupBuilder consumes HostOptions + IConfigurationManager +
        // ConfigRedactionEngine; no mutable per-instance state, safe as singleton.
        builder.Services.AddSingleton<Backup.ConfigRedactionEngine>();
        // Per-protocol redaction rules registry (ADR-0020 M-B). Composes any
        // registered IBundleRedactionRules (none until M-B sub-milestone B3)
        // over the shared baseline. Safe as a singleton — immutable after ctor.
        builder.Services.AddSingleton<Backup.BundleRedactionRulesRegistry>();
        builder.Services.AddSingleton<Backup.IBackupBuilder, Backup.BackupBuilder>();

        // Diagnostic bundle (ADR-0020 G1). Contributors are content-only; the
        // BundleBuilder composes them (in registration order) over the shared
        // redaction engine + registry. Order: identity, config, history, audit,
        // route-inventory.
        builder.Services.AddSingleton<Bundle.IBundleContributor, Bundle.GatewayIdentityContributor>();
        builder.Services.AddSingleton<Bundle.IBundleContributor, Bundle.ConfigContributor>();
        builder.Services.AddSingleton<Bundle.IBundleContributor, Bundle.HistoryContributor>();
        builder.Services.AddSingleton<Bundle.IBundleContributor, Bundle.AuditContributor>();
        builder.Services.AddSingleton<Bundle.IBundleContributor, Bundle.RouteInventoryContributor>();
        builder.Services.AddSingleton<Bundle.BundleBuilder>();

        // Commissioning checklist (M.1d). Pure composition over
        // IDiagnosticsService + IDiagnosticsEventAggregator +
        // IConfigurationManager + ILicenseManager. Stateless;
        // singleton-safe.
        builder.Services.AddSingleton<Checklist.IChecklistEvaluator, Checklist.ChecklistEvaluator>();

        // FOCAS2 Browse Controller probe (M.2b.3). Owns its own per-IP:Port
        // single-flight lease dictionary as instance state, so SINGLETON is
        // required — scoped or transient would defeat the lease. The
        // production constructor pulls ILicenseManager + IGatewayIdentity
        // from DI when present; both nullable so dev/sim runs work
        // license-free.
        builder.Services.AddSingleton<Api.Focas2BrowseService>();
        // MQTT Test Connection probe service (M.2b.6). Singleton for the
        // same per-broker single-flight reason as Focas2BrowseService.
        builder.Services.AddSingleton<Api.MqttTestConnectionService>();
        // Brother HTTP probe service (M.2d.2). Singleton for the same
        // per-BaseUrl single-flight reason. Requires IHttpClientFactory
        // for throwaway probe HTTP calls.
        builder.Services.AddHttpClient(nameof(Api.BrotherHttpProbeService));
        builder.Services.AddSingleton<Api.BrotherHttpProbeService>();
        // Modbus TCP probe service (M.2d.2). Same singleton pattern —
        // per IP:Port:UnitId single-flight. Uses FluentModbus directly
        // via FluentModbusProbeTransport; no IHttpClient needed.
        builder.Services.AddSingleton<Api.ModbusProbeService>();
        // OPC UA Client Test Connection + Browse services (PR 7c-2,
        // multi-protocol pilot Week 1). Same singleton pattern — per
        // endpoint URL single-flight; throwaway adapter / browse-service
        // per probe. License-gated on `source-opcua-client`.
        builder.Services.AddSingleton<Api.OpcUaClientTestConnectionService>();
        builder.Services.AddSingleton<Api.OpcUaClientBrowseApiService>();
        // MTConnect agent browse service (M.2b.4). One HTTP /probe GET per
        // call; license-gated on `source-mtconnect`. Singleton for symmetry.
        builder.Services.AddSingleton<Api.MTConnectBrowseService>();
        // Siemens S7 source wizard services (M.2b.2). The probe service owns
        // per host:port:rack:slot single-flight leases as instance state, so
        // SINGLETON — license-gated on `source-s7`. The address-validation
        // service is stateless parser-only (no PLC access, no license gate).
        builder.Services.AddSingleton<Api.S7ProbeService>();
        builder.Services.AddSingleton<Api.S7AddressValidationService>();
        // EtherNet/IP source wizard probe (multi-protocol pilot v2.1). The
        // probe owns per host|path|address single-flight leases as instance
        // state, so SINGLETON — license-gated on `source-ethernet-ip`.
        builder.Services.AddSingleton<Api.EthernetIpProbeService>();
        // MELSEC source wizard probe (UI slice). Singleton — owns per host:port
        // single-flight leases; license-gated on `source-melsec`. Probe-only
        // (no browse). Uses a short-lived SlmpClient; never mutates source state.
        builder.Services.AddSingleton<Api.MelsecProbeService>();
        // MELSEC observational diagnostics — resolves the running adapter via the
        // supervisor registry (read-only); degrades gracefully if unavailable.
        builder.Services.AddSingleton(sp => new Api.MelsecDiagnosticsService(
            sp.GetService<ElpisEdgeConnect.Host.Adapters.ISupervisedSourceRegistry>()));
        // License status + activation for the Studio License page. Singleton;
        // validates uploaded licenses (signature), writes to the license path,
        // and hot-reloads the live ILicenseManager. Buy URL from options, with an
        // EDGECONNECT_BUY_LICENSE_URL env override.
        builder.Services.AddSingleton(sp => new Api.LicenseActivationService(
            sp.GetRequiredService<ElpisEdgeConnect.Core.Licensing.ILicenseManager>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Api.LicenseActivationService>>(),
            Api.LicenseActivationService.ResolveLicensePath(),
            Environment.GetEnvironmentVariable("EDGECONNECT_BUY_LICENSE_URL") ?? resolved.BuyLicenseUrl,
            sp.GetRequiredService<ElpisEdgeConnect.Host.LicenseTrialState>()));
        // Buy License enquiry — serves Elpis contact details and emails enquiries
        // (SMTP when EDGECONNECT_SMTP_* is set, else a mailto fallback). Contact
        // values from options with env overrides.
        builder.Services.AddSingleton(sp => new Api.LicensePurchaseService(
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Api.LicensePurchaseService>>(),
            Environment.GetEnvironmentVariable("EDGECONNECT_SALES_EMAIL") ?? resolved.SalesEmail,
            Environment.GetEnvironmentVariable("EDGECONNECT_SALES_PHONE") ?? resolved.SalesPhone,
            Environment.GetEnvironmentVariable("EDGECONNECT_COMPANY_WEBSITE") ?? resolved.CompanyWebsite,
            sp.GetRequiredService<ElpisEdgeConnect.Core.Licensing.ILicenseManager>()));
        // Bulk-Source-Merge wizard services (chip 3 PR I-1). Service is
        // stateless across preview/submit calls; probe is operator-triggered
        // and never blocks submit. Both singletons for the same per-call
        // throwaway-state reason as the other browse / probe services.
        //
        // BulkSourceMergeService takes IConfigurationSchemaValidator as a
        // required constructor parameter; nobody else in the codebase
        // registers it in DI (Core's ConfigurationManager treats it as
        // optional with a NoOp fallback), so we register the NoOp here.
        // The real DataAnnotations + cross-record validation runs at
        // draft-apply time inside Core.ConfigurationManager, NOT at the
        // bulk-source-merge service's schema-check step — so a NoOp here
        // is consistent with what the rest of the runtime does. If a
        // future PR wires NJsonSchemaConfigurationValidator from
        // ElpisEdgeConnect.SchemaValidation into DI, this registration
        // can be deleted (TryAddSingleton wins for the real impl).
        builder.Services.TryAddSingleton<IConfigurationSchemaValidator>(
            _ => NoOpConfigurationSchemaValidator.Instance);
        builder.Services.AddSingleton<Api.BulkSourceMerge.BulkSourceMergeService>();
        builder.Services.AddSingleton<Api.BulkSourceMerge.BulkMTConnectProbeService>();
        // PR I-2 — wizard's HTTP client. Scoped: each Razor circuit gets its
        // own client tied to the per-circuit HttpClient (registered below).
        builder.Services.AddScoped<Wizards.IBulkSourceMergeClient, Wizards.HttpBulkSourceMergeClient>();

        // HttpClient for Blazor components to reach our own API. Same
        // BaseAddress as the listen endpoint — components stay
        // protocol-agnostic about being co-hosted with the API.
        builder.Services.AddScoped(sp =>
        {
            var opts = sp.GetRequiredService<ManagementOptions>();
            return new HttpClient { BaseAddress = new Uri(opts.BaseUrl) };
        });

        // Auth wire-up — only when explicitly enabled.
        if (resolved.Auth.Mode == ManagementAuthMode.Basic)
        {
            builder.Services
                .AddAuthentication(ManagementAuthSchemes.Basic)
                .AddScheme<AuthenticationSchemeOptions, BasicAuthenticationHandler>(
                    ManagementAuthSchemes.Basic, _ => { });
            builder.Services.AddAuthorization(o =>
            {
                o.DefaultPolicy = new AuthorizationPolicyBuilder(ManagementAuthSchemes.Basic)
                    .RequireAuthenticatedUser()
                    .Build();
                o.FallbackPolicy = o.DefaultPolicy;
            });
        }
        else
        {
            // No auth: explicit AllowAnonymous fallback so endpoint
            // ConventionBuilders don't try to demand a missing scheme.
            builder.Services.AddAuthorization(o =>
            {
                o.DefaultPolicy = new AuthorizationPolicyBuilder()
                    .RequireAssertion(_ => true)
                    .Build();
                o.FallbackPolicy = o.DefaultPolicy;
            });
        }

        // Hosted services.
        builder.Services.AddHostedService(sp =>
            new ManagementStartupAnnouncer(
                sp.GetRequiredService<ManagementOptions>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ManagementStartupAnnouncer>>(),
                meter: sp.GetService<System.Diagnostics.Metrics.Meter>()));

        return resolved;
    }

    /// <summary>
    /// Map endpoints + Blazor onto <paramref name="app"/>. Call after
    /// <see cref="AddConnectivityStudio"/>. No-op when registration
    /// was skipped (license-disabled).
    /// </summary>
    public static IEndpointRouteBuilder MapConnectivityStudio(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // The options singleton's absence signals "we got license-
        // skipped" — nothing to map.
        var options = app.Services.GetService<ManagementOptions>();
        if (options is null)
        {
            return app;
        }

        // Order matters: static files BEFORE auth so MudBlazor's
        // unauthenticated _content/* assets resolve even when basic
        // auth is enabled on the API + Blazor surface.
        app.UseStaticFiles();
        if (options.Auth.Mode == ManagementAuthMode.Basic)
        {
            app.UseAuthentication();
        }
        app.UseAuthorization();
        app.UseAntiforgery();

        // API docs: /swagger (Swagger UI) + /swagger/v1/swagger.json.
        app.UseSwagger();
        app.UseSwaggerUI(o =>
        {
            o.SwaggerEndpoint("/swagger/v1/swagger.json", "Management API v1");
            o.DocumentTitle = "Elpis EdgeConnect — Management API";
        });

        app.MapRoutesApi();
        app.MapSourcesApi();
        app.MapSourcesUpdateApi();
        app.MapSourcesDeleteApi();
        app.MapSinksApi();
        app.MapSinksUpdateApi();
        app.MapSinksDeleteApi();
        app.MapRoutesUpdateApi();
        app.MapRoutesDeleteApi();
        app.MapOnboardingApi();
        app.MapDiagnosticsApi();
        app.MapTapApi();
        app.MapBackupApi();
        app.MapBundleApi();
        app.MapChecklistApi();
        app.MapConfigApi();
        app.MapFocas2BrowseApi();
        app.MapMqttTestConnectionApi();
        app.MapBrotherHttpProbeApi();
        app.MapModbusProbeApi();
        app.MapOpcUaClientTestConnectionApi();
        app.MapOpcUaClientBrowseApi();
        app.MapMTConnectBrowseApi();
        app.MapBulkSourceMergeApi();
        app.MapS7ProbeApi();
        app.MapEthernetIpProbeApi();
        app.MapMelsecProbeApi();
        app.MapMelsecDiagnosticsApi();
        app.MapS7AddressValidationApi();
        app.MapS7TagTemplateApi();
        app.MapEnableDisableApi();
        app.MapLicenseApi();

        // Blazor server-side rendered components host. Pages land in
        // the App component (Components/App.razor).
        app.MapRazorComponents<Components.App>()
           .AddInteractiveServerRenderMode();

        return app;
    }

    private static ManagementOptions ApplyEnvOverrides(ManagementOptions baseOptions)
    {
        var bind = Environment.GetEnvironmentVariable("EDGECONNECT_MANAGEMENT_BIND");
        var portRaw = Environment.GetEnvironmentVariable("EDGECONNECT_MANAGEMENT_PORT");
        var result = baseOptions;
        if (!string.IsNullOrWhiteSpace(bind))
        {
            result = result with { BindAddress = bind };
        }
        if (!string.IsNullOrWhiteSpace(portRaw)
            && int.TryParse(portRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)
            && port is > 0 and <= 65535)
        {
            result = result with { Port = port };
        }
        return result;
    }
}
