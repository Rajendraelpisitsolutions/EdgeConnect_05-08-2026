// ============================================================================
// Tests: EnableDisableConfirmDrawerModel — pins the drawer's interaction
//        state machine (M.2b.6.1). Covers:
//
//   * Open transition + Ready state
//   * BeginApply locks Cancel + suppresses dismissal (Locked O)
//   * BeginApply duplicate-click ignored while Applying
//   * RecordApplied transitions to Closed (snackbar fires)
//   * RecordStaleView pivots primary text to "Refresh" (Locked G × O)
//   * RecordCrossRecord populates Blockers list
//   * TryDismiss suppressed during Applying (Locked O)
//   * Locked-N copy strings exact-match (snackbar variants, banners)
//   * Deep-link format per Locked C.1
// Reference: docs/sessions/2026-05-19-mp2b61-inline-enable-disable-plan-v3.md §3, §5, §6
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Management.Components.Shared;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class EnableDisableConfirmDrawerModelTests
{
    // ─── Open / Ready ───────────────────────────────────────────────────

    [Fact]
    public void Open_TransitionsToReady_WithImpactSummary()
    {
        var model = new EnableDisableConfirmDrawerModel();
        var impact = new ImpactSummary("Enabled: false → true", null, Array.Empty<DependencyRef>());

        model.Open(ConfigEntityKind.Source, "plc-1", "PLC 1", desiredEnabled: true, impact,
            expectedConfigurationVersion: "v-1");

        model.State.Should().Be(DrawerState.Ready);
        model.IsOpen.Should().BeTrue();
        model.IsApplying.Should().BeFalse();
        model.IsCancelEnabled.Should().BeTrue();
        model.IsPrimaryEnabled.Should().BeTrue();
        model.IsDismissalSuppressed.Should().BeFalse();
        model.Kind.Should().Be(ConfigEntityKind.Source);
        model.InstanceId.Should().Be("plc-1");
        model.DiffSummary.Should().Be("Enabled: false → true");
        model.ExpectedConfigurationVersion.Should().Be("v-1");
    }

    // ─── Locked O: loading discipline ───────────────────────────────────

    [Fact]
    public void BeginApply_TransitionsToApplying_LocksCancelAndSuppressesDismissal()
    {
        var model = MakeReady();

        var begun = model.BeginApply();

        begun.Should().BeTrue();
        model.State.Should().Be(DrawerState.Applying);
        model.IsApplying.Should().BeTrue();
        model.IsCancelEnabled.Should().BeFalse("Locked O — Cancel disabled during request");
        model.IsPrimaryEnabled.Should().BeFalse("Locked O — primary not re-clickable");
        model.IsDismissalSuppressed.Should().BeTrue("Locked O — drawer cannot close during request");
    }

    [Fact]
    public void BeginApply_DuplicateClickWhileApplying_IgnoredReturnsFalse()
    {
        // Locked O defence-in-depth: even if the button's disabled state
        // has a frame of timing slop, the model refuses to re-enter.
        var model = MakeReady();
        model.BeginApply();

        var second = model.BeginApply();

        second.Should().BeFalse();
        model.State.Should().Be(DrawerState.Applying, "no transition on duplicate-click");
    }

    [Fact]
    public void TryDismiss_DuringApplying_Suppressed()
    {
        var model = MakeReady();
        model.BeginApply();

        var dismissed = model.TryDismiss();

        dismissed.Should().BeFalse();
        model.State.Should().Be(DrawerState.Applying);
    }

    [Fact]
    public void TryDismiss_FromReady_ClosesDrawer()
    {
        var model = MakeReady();

        var dismissed = model.TryDismiss();

        dismissed.Should().BeTrue();
        model.State.Should().Be(DrawerState.Closed);
        model.IsOpen.Should().BeFalse();
    }

    // ─── Outcomes ───────────────────────────────────────────────────────

    [Fact]
    public void RecordApplied_TransitionsToClosed_AndCapturesAuditId()
    {
        var model = MakeReady();
        model.BeginApply();

        model.RecordApplied("audit-7c3a");

        model.State.Should().Be(DrawerState.Closed);
        model.LastAuditRecordId.Should().Be("audit-7c3a");
    }

    [Fact]
    public void RecordStaleView_TransitionsToStaleView_PivotsPrimaryToRefresh()
    {
        // Locked G × Locked O: stale-view response pivots the primary
        // button text to "Refresh"; Cancel re-enables.
        var model = MakeReady();
        model.BeginApply();

        model.RecordStaleView(currentVersion: "v-new");

        model.State.Should().Be(DrawerState.StaleView);
        model.PrimaryLabel.Should().Be("Refresh", "Locked G pivot — primary becomes Refresh on stale-view");
        model.IsPrimaryEnabled.Should().BeTrue();
        model.IsCancelEnabled.Should().BeTrue();
        model.CurrentVersion.Should().Be("v-new");
    }

    [Fact]
    public void RecordCrossRecord_TransitionsAndPopulatesBlockers()
    {
        var model = MakeReady();
        model.BeginApply();
        var blockers = new[]
        {
            new DependencyRef(ConfigEntityKind.Route, "r-1", "Route 1"),
            new DependencyRef(ConfigEntityKind.Route, "r-2", "Route 2"),
        };

        model.RecordCrossRecord(blockers);

        model.State.Should().Be(DrawerState.CrossRecord);
        model.Blockers.Should().HaveCount(2);
        model.Blockers.Should().BeEquivalentTo(blockers);
    }

    [Fact]
    public void RecordTimeout_TransitionsToTimeout_AndSurfacesLockedCopy()
    {
        var model = MakeReady();
        model.BeginApply();

        model.RecordTimeout();

        model.State.Should().Be(DrawerState.Timeout);
        model.ErrorMessage.Should().Be(EnableDisableConfirmDrawerModel.TimeoutBanner);
        model.IsCancelEnabled.Should().BeTrue();
        model.IsPrimaryEnabled.Should().BeTrue();
    }

    [Fact]
    public void RecordValidationError_TransitionsAndStoresMessage()
    {
        var model = MakeReady();
        model.BeginApply();

        model.RecordValidationError("Draft failed validation.");

        model.State.Should().Be(DrawerState.ValidationError);
        model.ErrorMessage.Should().Be("Draft failed validation.");
        model.IsPrimaryEnabled.Should().BeTrue();
    }

    // ─── Locked N: operator copy ────────────────────────────────────────

    [Theory]
    [InlineData(ConfigEntityKind.Source, "plc-1", true,  "Source 'plc-1' enabled.")]
    [InlineData(ConfigEntityKind.Source, "plc-1", false, "Source 'plc-1' disabled.")]
    [InlineData(ConfigEntityKind.Sink,   "mqtt-1", true,  "Destination 'mqtt-1' enabled.")]
    [InlineData(ConfigEntityKind.Sink,   "mqtt-1", false, "Destination 'mqtt-1' disabled.")]
    [InlineData(ConfigEntityKind.Route,  "r-1",    true,  "Route 'r-1' enabled.")]
    [InlineData(ConfigEntityKind.Route,  "r-1",    false, "Route 'r-1' disabled.")]
    public void AppliedSnackbarText_MatchesLockedNCopy(
        ConfigEntityKind kind, string id, bool enabled, string expected)
    {
        // Locked N: operator-facing words are exact. "Destination" for
        // Sink kind (operator-facing nomenclature) — not "Sink".
        var model = MakeReady(kind, id, enabled);

        model.AppliedSnackbarText.Should().Be(expected);
    }

    [Theory]
    [InlineData(ConfigEntityKind.Source, "plc-1", true,  "Source 'plc-1' is already enabled.")]
    [InlineData(ConfigEntityKind.Source, "plc-1", false, "Source 'plc-1' is already disabled.")]
    [InlineData(ConfigEntityKind.Sink,   "mqtt-1", true,  "Destination 'mqtt-1' is already enabled.")]
    [InlineData(ConfigEntityKind.Route,  "r-1",    false, "Route 'r-1' is already disabled.")]
    public void NoOpSnackbarText_MatchesLockedNCopy(
        ConfigEntityKind kind, string id, bool enabled, string expected)
    {
        var model = MakeReady(kind, id, enabled);

        model.NoOpSnackbarText.Should().Be(expected);
    }

    [Fact]
    public void StaleViewBanner_MatchesLockedNCopy()
    {
        EnableDisableConfirmDrawerModel.StaleViewBanner
            .Should().Be("Configuration changed. Refresh and try again.");
    }

    [Fact]
    public void TimeoutBanner_MatchesLockedOCopy()
    {
        EnableDisableConfirmDrawerModel.TimeoutBanner
            .Should().Be("Request timed out. The change may or may not have applied. Refresh to confirm.");
    }

    [Fact]
    public void Header_RendersOperationalEnglish()
    {
        var model = MakeReady(ConfigEntityKind.Route, "r-spindle", desiredEnabled: false);
        model.Header.Should().Be("Confirm: Disable route 'r-spindle'");
    }

    // ─── Locked C.1: focus-query deep links ─────────────────────────────

    [Theory]
    [InlineData(ConfigEntityKind.Source, "plc-1", "/sources?focus=plc-1")]
    [InlineData(ConfigEntityKind.Sink, "mqtt-1", "/sinks?focus=mqtt-1")]
    [InlineData(ConfigEntityKind.Route, "r-1", "/routes?focus=r-1")]
    public void DependencyDeepLink_UsesFocusQueryForm(
        ConfigEntityKind kind, string id, string expected)
    {
        var link = EnableDisableConfirmDrawerModel.DependencyDeepLink(
            new DependencyRef(kind, id, Name: null));
        link.Should().Be(expected);
    }

    [Fact]
    public void DependencyDeepLink_EscapesUrlSpecialCharacters()
    {
        // Defence: a route id with a slash or space wouldn't pass Core's
        // regex but if it ever did the link must escape correctly.
        var link = EnableDisableConfirmDrawerModel.DependencyDeepLink(
            new DependencyRef(ConfigEntityKind.Route, "r with space", null));
        link.Should().Contain("r%20with%20space");
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static EnableDisableConfirmDrawerModel MakeReady(
        ConfigEntityKind kind = ConfigEntityKind.Source,
        string instanceId = "plc-1",
        bool desiredEnabled = true)
    {
        var model = new EnableDisableConfirmDrawerModel();
        model.Open(kind, instanceId, displayName: instanceId, desiredEnabled,
            new ImpactSummary("Enabled: false → true", null, Array.Empty<DependencyRef>()),
            expectedConfigurationVersion: "v-1");
        return model;
    }
}
