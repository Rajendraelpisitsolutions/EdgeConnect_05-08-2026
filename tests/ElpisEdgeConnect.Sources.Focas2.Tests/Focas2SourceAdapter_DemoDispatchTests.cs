// ============================================================================
// Tests: Focas2SourceAdapter — production-ctor dispatch behaviour for
//        M.2b.3.1 demo mode. Pins:
//          * env var ON → adapter constructed with Focas2DemoApi
//          * env var OFF → adapter constructed with Focas2NativeApi
//          * end-to-end Initialize/Start/CheckHealth flow succeeds in
//            demo mode without DllNotFoundException (no real DLL needed)
//          * CheckHealthAsync surfaces metrics["demoMode"] = true when
//            backed by Focas2DemoApi (Q9)
//
//        Serialised via [Collection("Focas2DemoMode")] alongside
//        Focas2DemoModeOptionsTests — both manipulate the env-var cache.
// Reference: docs/sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v3.md
//            §5.4 / §1.4 (test set Focas2SourceAdapter_DemoDispatchTests)
// ============================================================================

using System;
using System.Threading;
using ElpisEdgeConnect.Sources.Focas2;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Sources.Focas2.Tests;

[Collection("Focas2DemoMode")]
public sealed class Focas2SourceAdapter_DemoDispatchTests
{
    [Fact]
    public async System.Threading.Tasks.Task ProductionCtor_WhenDemoModeOn_UsesFocas2DemoApi()
    {
        await WithEnvVarAsync("true", async () =>
        {
            await using var adapter = new Focas2SourceAdapter(
                instanceId: "demo-test",
                logger: NullLogger<Focas2SourceAdapter>.Instance);

            adapter.ApiForTesting.Should().BeOfType<Focas2DemoApi>(
                "Locked S: env var ON must dispatch to the synthetic backend");
        });
    }

    [Fact]
    public async System.Threading.Tasks.Task ProductionCtor_WhenDemoModeOff_UsesFocas2NativeApi()
    {
        await WithEnvVarAsync(null, async () =>
        {
            await using var adapter = new Focas2SourceAdapter(
                instanceId: "prod-test",
                logger: NullLogger<Focas2SourceAdapter>.Instance);

            adapter.ApiForTesting.Should().BeOfType<Focas2NativeApi>(
                "env var OFF must dispatch to the real native backend " +
                "(constructing Focas2NativeApi does not load the DLL — " +
                "only its METHOD calls would, and the test does not invoke any).");
        });
    }

    [Fact]
    public async System.Threading.Tasks.Task DemoMode_InitializeAndStart_SucceedsWithoutDllNotFoundException_AndExposesDemoModeMetric()
    {
        // End-to-end Locked I + Q9 proof. Set env var, construct adapter
        // via the production ctor, run Initialize → Start → CheckHealth.
        // The demo API succeeds without any native dependency, and
        // CheckHealthAsync must surface metrics["demoMode"] = true.
        await WithEnvVarAsync("true", async () =>
        {
            await using var adapter = new Focas2SourceAdapter(
                instanceId: "demo-end-to-end",
                logger: NullLogger<Focas2SourceAdapter>.Instance);

            var config = new Focas2SourceConfiguration
            {
                InstanceId = "demo-end-to-end",
                ProtocolName = Focas2SourceConfiguration.ProtocolNameConstant,
                DeviceId = "demo-end-to-end",
                IpAddress = "10.0.0.1",
            };

            await adapter.InitializeAsync(config, CancellationToken.None).ConfigureAwait(false);
            await adapter.StartAsync(CancellationToken.None).ConfigureAwait(false);

            var health = await adapter.CheckHealthAsync(CancellationToken.None).ConfigureAwait(false);

            var metrics = health.Metrics;
            metrics.Should().NotBeNull();
            metrics!.Should().ContainKey("demoMode");
            metrics!["demoMode"].Should().Be(true,
                "Q9: demo-backed adapters surface demoMode=true in health metrics " +
                "so per-source visibility complements the global Studio banner.");

            await adapter.StopAsync(CancellationToken.None).ConfigureAwait(false);
        });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static void WithEnvVar(string? value, Action act)
    {
        try
        {
            Focas2DemoModeOptions.Reset();
            Environment.SetEnvironmentVariable(Focas2DemoModeOptions.EnvVarName, value);
            act();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Focas2DemoModeOptions.EnvVarName, null);
            Focas2DemoModeOptions.Reset();
        }
    }

    private static async System.Threading.Tasks.Task WithEnvVarAsync(string? value, Func<System.Threading.Tasks.Task> act)
    {
        try
        {
            Focas2DemoModeOptions.Reset();
            Environment.SetEnvironmentVariable(Focas2DemoModeOptions.EnvVarName, value);
            await act().ConfigureAwait(false);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Focas2DemoModeOptions.EnvVarName, null);
            Focas2DemoModeOptions.Reset();
        }
    }
}
