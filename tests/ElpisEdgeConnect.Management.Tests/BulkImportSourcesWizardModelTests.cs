// ============================================================================
// File: BulkImportSourcesWizardModelTests.cs
// Purpose: POCO state-machine coverage for the bulk-import wizard. Verifies
//          step transitions, advance/back guards, error capture, and
//          finding-severity derivations. The HTTP client is a hand-rolled
//          fake — no Kestrel needed.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Management.Contracts.BulkSourceMerge;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class BulkImportSourcesWizardModelTests
{
    private sealed class FakeBulkSourceMergeClient : IBulkSourceMergeClient
    {
        public BulkSourceMergePreviewResponse? PreviewResult { get; set; }
        public BulkSourceMergeSubmitResponse? SubmitResult { get; set; }
        public Exception? PreviewError { get; set; }
        public Exception? SubmitError { get; set; }
        public List<BulkSourceMergePreviewRequest> PreviewRequests { get; } = new();
        public List<BulkSourceMergeSubmitRequest> SubmitRequests { get; } = new();

        public Task<BulkSourceMergePreviewResponse> PreviewAsync(
            BulkSourceMergePreviewRequest request, CancellationToken cancellationToken)
        {
            PreviewRequests.Add(request);
            if (PreviewError is not null) throw PreviewError;
            return Task.FromResult(PreviewResult ?? OkPreview());
        }

        public Task<BulkSourceMergeSubmitResponse> SubmitAsync(
            BulkSourceMergeSubmitRequest request, CancellationToken cancellationToken)
        {
            SubmitRequests.Add(request);
            if (SubmitError is not null) throw SubmitError;
            return Task.FromResult(SubmitResult ?? OkSubmit());
        }
    }

    private static BulkSourceMergePreviewResponse OkPreview() => new()
    {
        BaseConfigHash = "abc",
        ChosenSinkInstanceId = "acme-mqtt",
        ParsedRowCount = 1,
        Findings = Array.Empty<BulkSourceMergeFinding>(),
        CanSubmit = true,
    };

    private static BulkSourceMergeSubmitResponse OkSubmit() => new()
    {
        DraftId = "draft-1",
        Findings = Array.Empty<BulkSourceMergeFinding>(),
    };

    private static BulkImportSourcesWizardModel MakeModel(FakeBulkSourceMergeClient client) => new(client)
    {
        EnabledMqttSinkCount = 1,
        AvailableMqttSinks = new[] { "acme-mqtt" },
    };

    // ── Initial state ────────────────────────────────────────────────────────
    [Fact]
    public void Initial_StartsOnGatewayContext()
    {
        var m = MakeModel(new FakeBulkSourceMergeClient());
        m.CurrentStep.Should().Be(BulkImportWizardStep.GatewayContext);
        m.IsBusy.Should().BeFalse();
    }

    // ── Step 1 gating ────────────────────────────────────────────────────────
    [Fact]
    public void GatewayContext_NoSinks_CannotAdvance()
    {
        var m = new BulkImportSourcesWizardModel(new FakeBulkSourceMergeClient())
        {
            EnabledMqttSinkCount = 0,
        };
        m.CanAdvanceFromGatewayContext.Should().BeFalse();
        var act = () => m.AdvanceFromGatewayContext();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GatewayContext_WithSinks_AdvancesToProtocolPicker()
    {
        var m = MakeModel(new FakeBulkSourceMergeClient());
        m.AdvanceFromGatewayContext();
        m.CurrentStep.Should().Be(BulkImportWizardStep.ProtocolPicker);
    }

    // ── Step 2 gating ────────────────────────────────────────────────────────
    [Fact]
    public void Protocol_NoSelection_CannotAdvance()
    {
        var m = MakeModel(new FakeBulkSourceMergeClient());
        m.AdvanceFromGatewayContext();
        m.CanAdvanceFromProtocol.Should().BeFalse();
        var act = () => m.AdvanceFromProtocol();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Protocol_Selected_AdvancesToCsvUpload()
    {
        var m = MakeModel(new FakeBulkSourceMergeClient());
        m.AdvanceFromGatewayContext();
        m.SelectedProtocol = BulkSourceMergeProtocol.Focas2;
        m.AdvanceFromProtocol();
        m.CurrentStep.Should().Be(BulkImportWizardStep.CsvUpload);
    }

    // ── Step 3 gating ────────────────────────────────────────────────────────
    [Fact]
    public void CsvUpload_OneSinkAndCsv_CanAdvance()
    {
        var m = MakeModel(new FakeBulkSourceMergeClient());
        m.AdvanceFromGatewayContext();
        m.SelectedProtocol = BulkSourceMergeProtocol.Focas2;
        m.AdvanceFromProtocol();
        m.CsvBytes = new byte[] { 1, 2, 3 };
        m.CanAdvanceFromUpload.Should().BeTrue();
    }

    [Fact]
    public void CsvUpload_TwoPlusSinksRequireSelection_CannotAdvanceWithoutSink()
    {
        var m = new BulkImportSourcesWizardModel(new FakeBulkSourceMergeClient())
        {
            EnabledMqttSinkCount = 2,
            AvailableMqttSinks = new[] { "a", "b" },
            SelectedProtocol = BulkSourceMergeProtocol.Focas2,
            CsvBytes = new byte[] { 1 },
        };
        m.CanAdvanceFromUpload.Should().BeFalse();
        m.SelectedSinkInstanceId = "a";
        m.CanAdvanceFromUpload.Should().BeTrue();
    }

    [Fact]
    public async Task AdvanceFromUploadAsync_CallsPreviewAndStoresResult()
    {
        var client = new FakeBulkSourceMergeClient();
        var m = MakeModel(client);
        m.AdvanceFromGatewayContext();
        m.SelectedProtocol = BulkSourceMergeProtocol.Mtconnect;
        m.AdvanceFromProtocol();
        m.CsvBytes = new byte[] { 1, 2, 3 };
        m.ImportLabel = "test-batch";

        await m.AdvanceFromUploadAsync(CancellationToken.None);

        client.PreviewRequests.Should().ContainSingle();
        client.PreviewRequests[0].Protocol.Should().Be(BulkSourceMergeProtocol.Mtconnect);
        client.PreviewRequests[0].ImportLabel.Should().Be("test-batch");
        m.PreviewResult.Should().NotBeNull();
        m.CurrentStep.Should().Be(BulkImportWizardStep.Preview);
        m.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task AdvanceFromUploadAsync_PreviewThrows_CapturesLastErrorStaysOnUpload()
    {
        var client = new FakeBulkSourceMergeClient
        {
            PreviewError = new HttpRequestExceptionFake("Network down."),
        };
        var m = MakeModel(client);
        m.AdvanceFromGatewayContext();
        m.SelectedProtocol = BulkSourceMergeProtocol.Focas2;
        m.AdvanceFromProtocol();
        m.CsvBytes = new byte[] { 1 };

        await m.AdvanceFromUploadAsync(CancellationToken.None);

        m.LastError.Should().Contain("Network down");
        m.CurrentStep.Should().Be(BulkImportWizardStep.CsvUpload);
        m.PreviewResult.Should().BeNull();
        m.IsBusy.Should().BeFalse();
    }

    // ── Step 4 gating ────────────────────────────────────────────────────────
    [Fact]
    public void CanSubmit_ReflectsPreviewCanSubmit()
    {
        var client = new FakeBulkSourceMergeClient
        {
            PreviewResult = new BulkSourceMergePreviewResponse
            {
                BaseConfigHash = "x",
                ChosenSinkInstanceId = "y",
                ParsedRowCount = 1,
                Findings = Array.Empty<BulkSourceMergeFinding>(),
                CanSubmit = false,
            },
        };
        var m = MakeModel(client);
        m.CanSubmit.Should().BeFalse();
    }

    [Fact]
    public async Task AdvanceFromPreviewAsync_CallsSubmitWithBaseConfigHash()
    {
        var client = new FakeBulkSourceMergeClient
        {
            PreviewResult = new BulkSourceMergePreviewResponse
            {
                BaseConfigHash = "hash-from-server",
                ChosenSinkInstanceId = "acme-mqtt",
                ParsedRowCount = 3,
                Findings = Array.Empty<BulkSourceMergeFinding>(),
                CanSubmit = true,
            },
        };
        var m = MakeModel(client);
        m.AdvanceFromGatewayContext();
        m.SelectedProtocol = BulkSourceMergeProtocol.Focas2;
        m.AdvanceFromProtocol();
        m.CsvBytes = new byte[] { 1, 2, 3 };
        await m.AdvanceFromUploadAsync(CancellationToken.None);

        await m.AdvanceFromPreviewAsync(CancellationToken.None);

        client.SubmitRequests.Should().ContainSingle();
        client.SubmitRequests[0].BaseConfigHash.Should().Be("hash-from-server");
        m.SubmitResult.Should().NotBeNull();
        m.CurrentStep.Should().Be(BulkImportWizardStep.Confirmation);
        m.SubmitSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task AdvanceFromPreviewAsync_CannotSubmit_Throws()
    {
        var m = MakeModel(new FakeBulkSourceMergeClient());
        var act = async () => await m.AdvanceFromPreviewAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── Back / Reset ─────────────────────────────────────────────────────────
    [Fact]
    public void GoBack_OneStep()
    {
        var m = MakeModel(new FakeBulkSourceMergeClient());
        m.AdvanceFromGatewayContext();
        m.SelectedProtocol = BulkSourceMergeProtocol.Focas2;
        m.AdvanceFromProtocol();
        m.CurrentStep.Should().Be(BulkImportWizardStep.CsvUpload);
        m.GoBack();
        m.CurrentStep.Should().Be(BulkImportWizardStep.ProtocolPicker);
    }

    [Fact]
    public void GoBack_DoesNotGoBelowGatewayContext()
    {
        var m = MakeModel(new FakeBulkSourceMergeClient());
        m.GoBack();
        m.CurrentStep.Should().Be(BulkImportWizardStep.GatewayContext);
    }

    [Fact]
    public void ResetAll_ClearsState()
    {
        var m = MakeModel(new FakeBulkSourceMergeClient());
        m.SelectedProtocol = BulkSourceMergeProtocol.Focas2;
        m.CsvBytes = new byte[] { 1 };
        m.ImportLabel = "label";
        m.SelectedSinkInstanceId = "acme-mqtt";
        m.AdvanceFromGatewayContext();

        m.ResetAll();

        m.CurrentStep.Should().Be(BulkImportWizardStep.GatewayContext);
        m.SelectedProtocol.Should().BeNull();
        m.CsvBytes.Should().BeNull();
        m.ImportLabel.Should().BeNull();
        m.SelectedSinkInstanceId.Should().BeNull();
        m.PreviewResult.Should().BeNull();
        m.SubmitResult.Should().BeNull();
        m.LastError.Should().BeNull();
        m.IsBusy.Should().BeFalse();
    }

    // ── Finding derivations ──────────────────────────────────────────────────
    [Fact]
    public async Task Blockers_Warnings_SplitFromPreviewFindings()
    {
        var client = new FakeBulkSourceMergeClient
        {
            PreviewResult = new BulkSourceMergePreviewResponse
            {
                BaseConfigHash = "x",
                ChosenSinkInstanceId = "y",
                ParsedRowCount = 2,
                CanSubmit = false,
                Findings = new[]
                {
                    new BulkSourceMergeFinding { Code = "X.A", Message = "blocker A", Severity = BulkSourceMergeSeverity.Error },
                    new BulkSourceMergeFinding { Code = "X.B", Message = "warning B", Severity = BulkSourceMergeSeverity.Warning },
                    new BulkSourceMergeFinding { Code = "X.C", Message = "blocker C", Severity = BulkSourceMergeSeverity.Error },
                },
            },
        };
        var m = MakeModel(client);
        m.AdvanceFromGatewayContext();
        m.SelectedProtocol = BulkSourceMergeProtocol.Focas2;
        m.AdvanceFromProtocol();
        m.CsvBytes = new byte[] { 1 };
        await m.AdvanceFromUploadAsync(CancellationToken.None);

        m.Blockers.Should().HaveCount(2);
        m.Warnings.Should().ContainSingle().Which.Message.Should().Be("warning B");
    }

    [Fact]
    public void Blockers_Warnings_EmptyWhenNoPreviewYet()
    {
        var m = MakeModel(new FakeBulkSourceMergeClient());
        m.Blockers.Should().BeEmpty();
        m.Warnings.Should().BeEmpty();
    }

    private sealed class HttpRequestExceptionFake : Exception
    {
        public HttpRequestExceptionFake(string message) : base(message) { }
    }
}
