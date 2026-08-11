// ============================================================================
// Tests: ReloadOutcomePanelModel — view-model for the M.P2.2 phase 3
//        Studio reload-outcome panel. The Razor component is a thin
//        binding shell over this model, so pinning the model logic
//        gives bUnit-equivalent coverage of the panel's behaviour:
//
//          * Completed outcome → three chip rows + ElapsedMs display.
//          * InProgress outcome → banner text + diagnostics link.
//          * Skipped outcome → banner text with SupersededBy.
//          * Faulted chip click → /diagnostics#{instanceId} href.
//          * Null outcome → panel hidden entirely (load-bearing UX
//            distinction: null ≠ InProgress).
// ============================================================================

using System;
using ElpisEdgeConnect.Management.Components.Shared;
using ElpisEdgeConnect.Management.Contracts.Config;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class ReloadOutcomePanelModelTests
{
    [Fact]
    public void RendersAppliedRestartedFaultedChips_OnReloadCompleted()
    {
        var dto = new ReloadOutcomeDto
        {
            Status = "Completed",
            NewVersionId = "v-99",
            AppliedInstances = new[] { "src-new", "r-new" },
            RestartedInstances = new[] { "src-existing" },
            FaultedInstances = new[]
            {
                new FaultedReloadEntryDto
                {
                    InstanceId = "src-bad",
                    Kind = "Source",
                    ErrorCode = "HOST.RECONCILE_FAILED",
                    Message = "boom",
                },
            },
            ElapsedMs = 142,
        };

        var model = new ReloadOutcomePanelModel(dto);

        model.Visible.Should().BeTrue();
        model.IsCompleted.Should().BeTrue();
        model.IsInProgress.Should().BeFalse();
        model.IsSkipped.Should().BeFalse();
        model.AppliedInstances.Should().BeEquivalentTo(new[] { "src-new", "r-new" });
        model.RestartedInstances.Should().BeEquivalentTo(new[] { "src-existing" });
        model.FaultedInstances.Should().ContainSingle(f => f.InstanceId == "src-bad");
        model.ElapsedMsDisplay.Should().Be("142 ms");
    }

    [Fact]
    public void RendersInProgressBanner_WithDiagnosticsLink()
    {
        var dto = new ReloadOutcomeDto
        {
            Status = "InProgress",
            NewVersionId = "v-100",
            AppliedInstances = Array.Empty<string>(),
            RestartedInstances = Array.Empty<string>(),
            FaultedInstances = Array.Empty<FaultedReloadEntryDto>(),
            ElapsedMs = 10_000,
        };

        var model = new ReloadOutcomePanelModel(dto);

        model.Visible.Should().BeTrue();
        model.IsInProgress.Should().BeTrue();
        model.IsCompleted.Should().BeFalse();
        model.IsSkipped.Should().BeFalse();
        // The banner text directs the operator at diagnostics — the
        // exact string is pinned because docs & runbook reference it.
        ReloadOutcomePanelModel.InProgressMessage
            .Should().Contain("Diagnostics", "the InProgress banner must point operators at the durable surface");
    }

    [Fact]
    public void RendersSkippedBanner_WithSupersededVersion()
    {
        var dto = new ReloadOutcomeDto
        {
            Status = "Skipped",
            NewVersionId = "v-stale",
            AppliedInstances = Array.Empty<string>(),
            RestartedInstances = Array.Empty<string>(),
            FaultedInstances = Array.Empty<FaultedReloadEntryDto>(),
            SupersededBy = "v-newer",
            ElapsedMs = 0,
        };

        var model = new ReloadOutcomePanelModel(dto);

        model.Visible.Should().BeTrue();
        model.IsSkipped.Should().BeTrue();
        model.SupersededByVersion.Should().Be("v-newer");
        model.SkippedMessage.Should().Contain("v-newer");
    }

    [Fact]
    public void FaultedChipHref_PointsToDiagnostics()
    {
        // The clickable faulted chip navigates to the diagnostics page
        // focused on the offending instance — the operator's path from
        // outcome surface back into the durable diagnostics surface.
        var href = ReloadOutcomePanelModel.DiagnosticsHrefFor("modbus-line-3");

        href.Should().Be("/diagnostics#modbus-line-3");
    }

    [Fact]
    public void NullOutcome_PanelIsHidden_PreservingNullVsInProgressDistinction()
    {
        // CRITICAL UX INVARIANT: a null DTO (= no IReloadOutcomeRegistry
        // wired) hides the panel ENTIRELY. The operator must NOT see
        // an "InProgress" placeholder for a state that actually means
        // "no observation surface". Pinning this prevents a future
        // refactor from collapsing the two states.
        var model = new ReloadOutcomePanelModel(null);

        model.Visible.Should().BeFalse();
        model.IsCompleted.Should().BeFalse();
        model.IsInProgress.Should().BeFalse();
        model.IsSkipped.Should().BeFalse();
        model.AppliedInstances.Should().BeEmpty();
        model.RestartedInstances.Should().BeEmpty();
        model.FaultedInstances.Should().BeEmpty();
        model.SupersededByVersion.Should().BeNull();
        model.ElapsedMs.Should().BeNull();
        model.ElapsedMsDisplay.Should().BeEmpty();
    }
}
