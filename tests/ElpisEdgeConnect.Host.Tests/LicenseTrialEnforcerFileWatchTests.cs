// ============================================================================
// File: LicenseTrialEnforcerFileWatchTests.cs
// Purpose: Guards SyncLicenseFileAsync's "have I already processed this exact
//          file?" check.
//
//          The enforcer stamps the license file's write time and length after
//          each attempt so an unreadable file is not re-read every cycle. That
//          stamping was defeated for FAILING files: the failure path calls
//          ILicenseManager.Unload(), and the guard also required
//          _license.Current to be non-null, so `unchanged` evaluated false on
//          every subsequent tick. A corrupt, tampered, or wrong-gateway license
//          was therefore re-read and re-logged at error level once per second —
//          measured at ~19 lines per 20s against a live gateway, roughly 86,000
//          a day from a single bad file.
//
//          An EXPIRED license is not affected: it loads successfully (status
//          Expired) and so was always stamped correctly. The bug only ever hit
//          files that throw on load.
// Reference: docs/decisions/0035-unlicensed-runtime-cutoff.md
// ============================================================================

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Diagnostics;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host;
using ElpisEdgeConnect.Host.Adapters;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace ElpisEdgeConnect.Host.Tests;

public sealed class LicenseTrialEnforcerFileWatchTests : IDisposable
{
    private readonly string _dir;
    private readonly string _licensePath;

    public LicenseTrialEnforcerFileWatchTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ec-lic-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _licensePath = Path.Combine(_dir, "edgelicense.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task SyncLicenseFile_UnreadableFileUnchanged_IsNotReReadEveryTick()
    {
        File.WriteAllText(_licensePath, "{ not a license }");

        var license = Substitute.For<ILicenseManager>();
        license.Current.Returns((LicenseInfo?)null);
        license.Status.Returns(LicenseStatus.NotLoaded);
        license.LoadFromFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("corrupt")));

        var enforcer = CreateEnforcer(license);

        await enforcer.SyncLicenseFileAsync(CancellationToken.None);
        await enforcer.SyncLicenseFileAsync(CancellationToken.None);
        await enforcer.SyncLicenseFileAsync(CancellationToken.None);

        await license.Received(1).LoadFromFileAsync(_licensePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncLicenseFile_UnreadableFileThenReplaced_IsReReadOnce()
    {
        File.WriteAllText(_licensePath, "{ not a license }");

        var license = Substitute.For<ILicenseManager>();
        license.Current.Returns((LicenseInfo?)null);
        license.Status.Returns(LicenseStatus.NotLoaded);
        license.LoadFromFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("corrupt")));

        var enforcer = CreateEnforcer(license);

        await enforcer.SyncLicenseFileAsync(CancellationToken.None);
        await enforcer.SyncLicenseFileAsync(CancellationToken.None);

        // Operator replaces the bad file. Length differs, so the stamp no longer
        // matches and the new content must be picked up on the very next tick.
        File.WriteAllText(_licensePath, "{ a different, longer payload than before }");

        await enforcer.SyncLicenseFileAsync(CancellationToken.None);

        await license.Received(2).LoadFromFileAsync(_licensePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncLicenseFile_DeletedThenRestored_ReloadsTheRestoredFile()
    {
        File.WriteAllText(_licensePath, "{ a license }");

        // A REAL LicenseInfo: it is a sealed record, so NSubstitute cannot proxy
        // it and Current would silently stay null — which is exactly what made
        // the delete branch look like it never ran.
        var info = new LicenseInfo
        {
            LicenseId = "TEST",
            Customer = "Test",
            GatewayId = "gw-test",
            Edition = LicenseEdition.Enterprise,
            IssuedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            Limits = new LicenseLimits { MaxSourceInstances = 10, MaxSinkInstances = 10, MaxRoutes = 10 },
            Modules = FrozenDictionary<string, LicenseModule>.Empty,
        };

        var loaded = false;
        var license = Substitute.For<ILicenseManager>();
        license.Current.Returns(_ => loaded ? info : null);
        license.Status.Returns(LicenseStatus.Valid);
        license.LoadFromFileAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { loaded = true; return Task.CompletedTask; });
        license.When(l => l.Unload()).Do(_ => loaded = false);

        var enforcer = CreateEnforcer(license);

        await enforcer.SyncLicenseFileAsync(CancellationToken.None);   // loads
        await enforcer.SyncLicenseFileAsync(CancellationToken.None);   // unchanged, skipped

        File.Delete(_licensePath);
        await enforcer.SyncLicenseFileAsync(CancellationToken.None);   // discards

        File.WriteAllText(_licensePath, "{ a license }");
        await enforcer.SyncLicenseFileAsync(CancellationToken.None);   // must reload

        await license.Received(2).LoadFromFileAsync(_licensePath, Arg.Any<CancellationToken>());
        license.Received(1).Unload();
    }

    private LicenseTrialEnforcer CreateEnforcer(ILicenseManager license) =>
        new(
            license,
            new SourceSupervisor(
                Array.Empty<SourceRegistration>(),
                Substitute.For<ISourceHealthSink>(),
                NullLogger<SourceSupervisor>.Instance),
            new SinkSupervisor(
                Array.Empty<SinkRegistration>(),
                Substitute.For<ISinkHealthSink>(),
                NullLogger<SinkSupervisor>.Instance),
            new LicenseTrialState(),
            Substitute.For<IDiagnosticsService>(),
            NullLogger<LicenseTrialEnforcer>.Instance,
            licensePath: _licensePath);
}
