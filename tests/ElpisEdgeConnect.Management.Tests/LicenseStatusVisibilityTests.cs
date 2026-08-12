// ============================================================================
// Tests: LicenseActivationService.GetStatus visibility rules.
//
// Pins WHICH operator actions the License page offers, per license state.
// The rule under test is the clock-tamper suppression: a rollback makes
// LicenseTrialEnforcer.CheckClockRollback call ILicenseManager.Unload(), so a
// gateway with a perfectly good license reports NotLoaded. Read naively, the
// visibility rules then offer "Activate License" and "Buy License" — inviting
// the operator to upload a second copy of the licence they already have, or to
// purchase another. Neither clears the condition: the next enforcer tick
// unloads the replacement identically, because the fault is the system clock.
// Only correcting the date/time helps, which is what the tamper alert says.
//
// The non-tampered cases are kept alongside so the suppression cannot be
// widened by accident into "never offer activation".
// Reference: docs/decisions/0035-unlicensed-runtime-cutoff.md
// ============================================================================

using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Host;
using ElpisEdgeConnect.Management.Api;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class LicenseStatusVisibilityTests
{
    /// <summary>
    /// Build the service over a manager with nothing loaded — the state a
    /// gateway lands in both when no license file exists and when the enforcer
    /// has unloaded one it refuses to honour.
    /// </summary>
    private static (LicenseActivationService Service, LicenseTrialState Trial) CreateSut()
    {
        var trial = new LicenseTrialState();
        var service = new LicenseActivationService(
            new LicenseManager(),
            NullLogger<LicenseActivationService>.Instance,
            licensePath: Path.Combine(Path.GetTempPath(), "edgelicense.json"),
            buyUrl: null,
            trialState: trial);
        return (service, trial);
    }

    [Fact]
    public void NotLoaded_without_tampering_offers_both_activate_and_buy()
    {
        var (service, _) = CreateSut();

        var status = service.GetStatus();

        status.Status.Should().Be(nameof(LicenseStatus.NotLoaded));
        status.ClockTampered.Should().BeFalse();
        status.ShowActivate.Should().BeTrue("an unlicensed gateway must still offer activation");
        status.ShowBuy.Should().BeTrue("an unlicensed gateway must still offer purchase");
    }

    [Fact]
    public void Clock_tampering_suppresses_both_activate_and_buy()
    {
        var (service, trial) = CreateSut();
        trial.SetClockTampered(true);

        var status = service.GetStatus();

        status.ClockTampered.Should().BeTrue();
        status.ShowActivate.Should().BeFalse(
            "uploading another license cannot clear a clock rollback — the next tick unloads it too");
        status.ShowBuy.Should().BeFalse(
            "buying a license cannot clear a clock rollback; the fix is to correct the system clock");
    }

    [Fact]
    public void Correcting_the_clock_restores_both_actions()
    {
        var (service, trial) = CreateSut();
        trial.SetClockTampered(true);
        service.GetStatus().ShowActivate.Should().BeFalse();

        // The enforcer clears the flag on the first sane tick after the clock is
        // corrected. Nothing else has to happen for the page to become useful
        // again — no restart, no re-upload.
        trial.SetClockTampered(false);

        var status = service.GetStatus();
        status.ClockTampered.Should().BeFalse();
        status.ShowActivate.Should().BeTrue();
        status.ShowBuy.Should().BeTrue();
    }
}
