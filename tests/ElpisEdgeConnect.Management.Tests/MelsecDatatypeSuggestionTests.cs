// ============================================================================
// Tests: MELSEC wizard datatype suggestion — the tag table's "Datatype" cell is
//        pre-filled from the address the operator types (bit device / word-bit
//        form -> Bool, word device -> Int16), following the S7 wizard's
//        suggest-never-coerce convention:
//          * only an untouched (null/blank) cell is filled;
//          * an operator-chosen datatype is never overwritten;
//          * an unparseable (half-typed) address suggests nothing and is NOT an
//            error — the wizard explains the absence instead.
//        Suggestions are produced by the REAL backend parser and are always a
//        member of the wizard's datatype dropdown list (a value outside the list
//        renders as a blank cell, which is worse than no suggestion).
// ============================================================================

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Components.Pages.SourceWizards;
using ElpisEdgeConnect.Management.Wizards;
using ElpisEdgeConnect.Sources.Melsec.Profiles;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class MelsecDatatypeSuggestionTests
{
    // ── SuggestDatatype: address -> datatype ──────────────────────────────

    [Theory]
    [InlineData("M0")]      // internal relay
    [InlineData("M1234")]
    [InlineData("X1F")]     // hex-radix input
    [InlineData("Y10")]
    [InlineData("B1A")]
    [InlineData("SM400")]   // special relay (bit)
    [InlineData("TS5")]     // timer contact (bit)
    public void SuggestDatatype_BitDevice_SuggestsBool(string address)
    {
        var suggestion = MelsecSourceWizardModel.SuggestDatatype(address);

        suggestion.Should().Be("Bool");
    }

    [Theory]
    [InlineData("D100")]
    [InlineData("W1A")]     // hex-radix word
    [InlineData("R10")]
    [InlineData("ZR16384")]
    [InlineData("SD1")]
    [InlineData("TN5")]     // timer current value (single word)
    public void SuggestDatatype_WordDevice_SuggestsWordDefault(string address)
    {
        var suggestion = MelsecSourceWizardModel.SuggestDatatype(address);

        suggestion.Should().Be("Int16");
    }

    [Theory]
    [InlineData("D100.5")]
    [InlineData("D100.F")]  // bit index is hexadecimal 0..F
    [InlineData("W1A.0")]
    public void SuggestDatatype_WordBitForm_SuggestsBool(string address)
    {
        var suggestion = MelsecSourceWizardModel.SuggestDatatype(address);

        suggestion.Should().Be("Bool");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("D")]       // half-typed: device symbol, no number yet
    [InlineData("QQ7")]     // unknown device
    [InlineData("L5")]      // recognized but not implemented in Slice 1
    [InlineData("T100")]    // ambiguous bare timer prefix
    [InlineData("D100.G")]  // invalid bit index
    public void SuggestDatatype_UnparseableAddress_ReturnsNull(string? address)
    {
        var suggestion = MelsecSourceWizardModel.SuggestDatatype(address);

        suggestion.Should().BeNull();
    }

    [Fact]
    public void SuggestDatatype_ProfileExcludesTheDevice_ReturnsNull()
    {
        // ZR is not available on the iQ-F / FX5 profile, so nothing is inferable
        // there even though the same address suggests Int16 on Modern.
        MelsecSourceWizardModel.SuggestDatatype("ZR16384", MelsecProfiles.Modern).Should().Be("Int16");

        MelsecSourceWizardModel.SuggestDatatype("ZR16384", MelsecProfiles.IqF).Should().BeNull();
    }

    [Theory]
    [InlineData("M0")]
    [InlineData("D100")]
    [InlineData("D100.5")]
    [InlineData("ZR16384")]
    public void SuggestDatatype_AnySuggestion_IsOfferedByTheDropdown(string address)
    {
        // A suggestion outside the dropdown's option list renders as a blank
        // cell — worse than no suggestion at all.
        var suggestion = MelsecSourceWizardModel.SuggestDatatype(address);

        MelsecSourceWizardModel.Datatypes.Should().Contain(suggestion!);
    }

    [Theory]
    [InlineData("M0")]
    [InlineData("D100")]
    [InlineData("D100.5")]
    [InlineData("TN5")]
    public void SuggestDatatype_AppliedSuggestion_LeavesNoDatatypeValidationError(string address)
    {
        // The suggestion comes from the same parser validation uses, so applying
        // it must never produce the "datatype mismatch" the operator would then
        // have to fix by hand.
        var row = new MelsecTagWizardRow { Name = "t", Address = address, ScanRateMs = 1000 };

        MelsecSourceWizardModel.TryApplyDatatypeSuggestion(row).Should().BeTrue();

        MelsecSourceWizardModel.ValidateTag(row).Should().NotContain(issue => issue.Path == "Datatype");
    }

    // ── TryApplyDatatypeSuggestion: suggest, never coerce ─────────────────

    [Fact]
    public void TryApplyDatatypeSuggestion_EmptyCellAndBitAddress_FillsBool()
    {
        var row = new MelsecTagWizardRow { Name = "run", Address = "M0", ScanRateMs = 1000 };

        var applied = MelsecSourceWizardModel.TryApplyDatatypeSuggestion(row);

        applied.Should().BeTrue();
        row.Datatype.Should().Be("Bool");
    }

    [Fact]
    public void TryApplyDatatypeSuggestion_EmptyCellAndWordAddress_FillsWordDefault()
    {
        var row = new MelsecTagWizardRow { Name = "cycles", Address = "D100", ScanRateMs = 1000 };

        var applied = MelsecSourceWizardModel.TryApplyDatatypeSuggestion(row);

        applied.Should().BeTrue();
        row.Datatype.Should().Be("Int16");
    }

    [Fact]
    public void TryApplyDatatypeSuggestion_WhitespaceCell_IsTreatedAsUntouched()
    {
        var row = new MelsecTagWizardRow { Name = "cycles", Address = "D100", Datatype = "  ", ScanRateMs = 1000 };

        var applied = MelsecSourceWizardModel.TryApplyDatatypeSuggestion(row);

        applied.Should().BeTrue();
        row.Datatype.Should().Be("Int16");
    }

    [Theory]
    [InlineData("UInt16")]
    [InlineData("Float32")]
    [InlineData("Bool")]
    public void TryApplyDatatypeSuggestion_OperatorChoseADatatype_NeverOverwritesIt(string chosen)
    {
        // The address would suggest Int16 (word) — the operator's choice wins,
        // even when it disagrees with the address.
        var row = new MelsecTagWizardRow { Name = "cycles", Address = "D100", Datatype = chosen, ScanRateMs = 1000 };

        var applied = MelsecSourceWizardModel.TryApplyDatatypeSuggestion(row);

        applied.Should().BeFalse();
        row.Datatype.Should().Be(chosen);
    }

    [Theory]
    [InlineData("")]
    [InlineData("D")]
    [InlineData("L5")]
    public void TryApplyDatatypeSuggestion_UnparseableAddress_LeavesCellNullAndRaisesNoError(string address)
    {
        var row = new MelsecTagWizardRow { Name = "half-typed", Address = address, ScanRateMs = 1000 };

        var applied = MelsecSourceWizardModel.TryApplyDatatypeSuggestion(row);

        applied.Should().BeFalse();
        row.Datatype.Should().BeNull();
        MelsecSourceWizardModel.SuggestDatatype(address).Should().BeNull();
    }

    [Fact]
    public void TryApplyDatatypeSuggestion_ProfileAware_UsesTheSelectedFamily()
    {
        // Same operator address, two families: iQ-F X/Y labels are octal, and X
        // is a bit device on both — but ZR only exists on Modern.
        var zrRow = new MelsecTagWizardRow { Name = "recipe", Address = "ZR16384", ScanRateMs = 1000 };
        MelsecSourceWizardModel.TryApplyDatatypeSuggestion(zrRow, MelsecProfiles.IqF).Should().BeFalse();
        zrRow.Datatype.Should().BeNull();

        var xRow = new MelsecTagWizardRow { Name = "input_a", Address = "X10", ScanRateMs = 1000 };
        MelsecSourceWizardModel.TryApplyDatatypeSuggestion(xRow, MelsecProfiles.IqF).Should().BeTrue();
        xRow.Datatype.Should().Be("Bool");
    }
}

/// <summary>
/// Render-level cover for the two captions that make the Datatype cell
/// self-explanatory: the applied-suggestion confirmation, and the explicit
/// "nothing inferable" hint that distinguishes an empty cell from a broken one.
/// </summary>
public sealed class MelsecDatatypeSuggestionRenderTests : TestContext
{
    public MelsecDatatypeSuggestionRenderTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new HttpClient(new EmptyHandler()) { BaseAddress = new Uri("http://localhost") });
    }

    private sealed class EmptyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            });
    }

    // Edit mode renders without the Add-mode /api/v1/config fetch.
    private IRenderedFragment RenderEdit(SourceInstanceConfig cfg) => Render(builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<AddMelsecSource>(1);
        builder.AddAttribute(2, "EditMode", EditModeContext.Edit(cfg.InstanceId, "v1"));
        builder.AddAttribute(3, "HydratedConfig", cfg);
        builder.CloseComponent();
    });

    private static SourceInstanceConfig ConfigWithTag(string address, string? datatype)
    {
        var model = new MelsecSourceWizardModel { InstanceId = "melsec-1", Host = "10.0.0.5", Port = 5007 };
        model.Tags.Add(new MelsecTagWizardRow { Name = "t1", Address = address, Datatype = datatype, ScanRateMs = 1000 });
        return model.BuildSourceInstance();
    }

    [Fact]
    public void ParseableAddress_ShowsTheSuggestionCaption()
    {
        var cut = RenderEdit(ConfigWithTag("D100", "Int16"));

        cut.Find("[data-testid='melsec-datatype-suggestion']")
            .TextContent.Should().Contain("Address suggests: Int16");
    }

    [Fact]
    public void BitAddress_SuggestionCaptionNamesBool()
    {
        var cut = RenderEdit(ConfigWithTag("M0", "Bool"));

        cut.Find("[data-testid='melsec-datatype-suggestion']")
            .TextContent.Should().Contain("Address suggests: Bool");
    }

    [Fact]
    public void UninferableAddressWithEmptyDatatype_ExplainsTheBlankCell()
    {
        // "D" is half-typed: no suggestion caption, and the blank Datatype cell
        // says why it is blank rather than looking broken.
        var cut = RenderEdit(ConfigWithTag("D", datatype: null));

        cut.FindAll("[data-testid='melsec-datatype-suggestion']").Should().BeEmpty();
        cut.Find("[data-testid='melsec-datatype-not-inferred']")
            .TextContent.Should().Contain("choose a datatype");
    }

    [Fact]
    public void TypingAnAddress_FillsTheEmptyDatatypeCell()
    {
        // End-to-end: the reported bug was that the Datatype cell stayed empty
        // (row invalid) no matter what address was typed. A row that is complete
        // except for its address must become valid once a word address arrives.
        var cut = RenderEdit(ConfigWithTag(string.Empty, datatype: null));
        cut.Markup.Should().Contain("✗ Error");

        var addressInput = cut.Find("input[aria-label='MELSEC address']");
        addressInput.Input("D100");
        addressInput.Change("D100");

        // The address field is debounced (350 ms), so wait for the edit to land
        // rather than sleeping — the assertion itself is the completion signal.
        cut.WaitForAssertion(
            () =>
            {
                cut.Markup.Should().Contain("Address suggests: Int16");
                cut.Markup.Should().Contain("✓ Valid");
                cut.Markup.Should().NotContain("✗ Error");
            },
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void EmptyAddress_ShowsNeitherCaption()
    {
        var cut = RenderEdit(ConfigWithTag(string.Empty, datatype: null));

        cut.FindAll("[data-testid='melsec-datatype-suggestion']").Should().BeEmpty();
        cut.FindAll("[data-testid='melsec-datatype-not-inferred']").Should().BeEmpty();
    }
}
