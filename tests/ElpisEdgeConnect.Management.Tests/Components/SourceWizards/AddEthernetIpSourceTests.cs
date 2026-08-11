// ============================================================================
// Tests: AddEthernetIpSource render — the note beside a tag row's datatype.
//
// SuggestDatatype returning null for an ordinary Logix name (Tag1,
// Machine_Value, Line3_Out) is deliberate: a confidently wrong type is worse
// than an empty cell. What was missing is that the wizard never said it had
// looked and could not tell, which is what made a considered limitation read
// as a bug. These render tests pin both halves — the "could not tell" note and
// the "this came from the name" caption — and pin that neither fires on a row
// the operator has not reached yet.
//
// Rendered in Edit mode (which skips the Add-mode /api/v1/config fetch) so no
// HTTP is needed.
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
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests.Components.SourceWizards;

public sealed class AddEthernetIpSourceTests : TestContext
{
    private const string SuggestedNote = "[data-testid='ethernetip-datatype-suggested']";
    private const string NotInferredNote = "[data-testid='ethernetip-datatype-not-inferred']";

    public AddEthernetIpSourceTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new HttpClient(new EmptyHandler())
        {
            BaseAddress = new Uri("http://localhost"),
        });
    }

    // Edit mode issues no requests during render; the probe endpoint is not
    // exercised here, so an empty body is enough.
    private sealed class EmptyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private static SourceInstanceConfig ConfigWithTag(string address, string? datatype)
    {
        var model = new EthernetIpSourceWizardModel
        {
            InstanceId = "plc-line-3",
            DeviceId = "plc-line-3",
            DeviceName = "Line 3 PLC",
            Host = "10.0.0.50",
        };
        model.Tags.Add(new EthernetIpTagWizardRow
        {
            Name = "tag1",
            Address = address,
            Datatype = datatype,
            ScanRateMs = 1000,
        });
        return model.BuildSourceInstance();
    }

    private IRenderedFragment RenderEdit(SourceInstanceConfig cfg) => Render(builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<AddEthernetIpSource>(1);
        builder.AddAttribute(2, "EditMode", EditModeContext.Edit(cfg.InstanceId, "v1"));
        builder.AddAttribute(3, "HydratedConfig", cfg);
        builder.CloseComponent();
    });

    [Theory]
    [InlineData("Tag1")]
    [InlineData("Machine_Value")]
    [InlineData("Line3_Out")]
    public void DatatypeNote_NameMatchesNoWordList_ShowsCannotInferHint(string address)
    {
        var cut = RenderEdit(ConfigWithTag(address, datatype: null));

        cut.FindAll(NotInferredNote).Should().NotBeEmpty();
        cut.Markup.Should().Contain("infer from this tag name");
    }

    [Fact]
    public void DatatypeNote_BlankAddress_ShowsNoHint()
    {
        // A row the operator has not reached yet is not a failed reading.
        var cut = RenderEdit(ConfigWithTag(string.Empty, datatype: null));

        cut.FindAll(NotInferredNote).Should().BeEmpty();
        cut.FindAll(SuggestedNote).Should().BeEmpty();
    }

    [Fact]
    public void DatatypeNote_DatatypeAlreadySet_ShowsNoHint()
    {
        var cut = RenderEdit(ConfigWithTag("Tag1", datatype: "DINT"));

        cut.FindAll(NotInferredNote).Should().BeEmpty();
        // A hydrated value is a decision, not a guess — it is not captioned.
        cut.FindAll(SuggestedNote).Should().BeEmpty();
    }

    [Fact]
    public void DatatypeNote_HintIsGuidance_RendersQuietlyAndNotInTheErrorPalette()
    {
        var cut = RenderEdit(ConfigWithTag("Tag1", datatype: null));

        var style = cut.Find(NotInferredNote).GetAttribute("style") ?? string.Empty;
        style.Should().NotContain("error");
        style.Should().Contain("text-secondary");
        // Not an alert / banner — it is a caption beside the row.
        cut.Find(NotInferredNote).ClassName.Should().NotContain("mud-alert");
    }

    [Fact]
    public void AddressEntered_NameMatchesWordList_AppliesSuggestionAndCaptionsIt()
    {
        var cut = RenderEdit(ConfigWithTag(string.Empty, datatype: null));
        cut.FindAll(SuggestedNote).Should().BeEmpty();

        cut.Find("input[aria-label='EtherNet/IP address']").Change("Motor_Temp");

        cut.FindAll(SuggestedNote).Should().NotBeEmpty();
        cut.Markup.Should().Contain("Suggested from the tag name");
        cut.FindAll(NotInferredNote).Should().BeEmpty();
    }

    [Fact]
    public void AddressEntered_NameMatchesNoWordList_ShowsHintInsteadOfCaption()
    {
        var cut = RenderEdit(ConfigWithTag(string.Empty, datatype: null));

        cut.Find("input[aria-label='EtherNet/IP address']").Change("Tag1");

        cut.FindAll(NotInferredNote).Should().NotBeEmpty();
        cut.FindAll(SuggestedNote).Should().BeEmpty();
    }

    [Fact]
    public void AddressEntered_OperatorAlreadyChoseDatatype_KeepsChoiceAndShowsNoNote()
    {
        var cut = RenderEdit(ConfigWithTag(string.Empty, datatype: "STRING"));

        cut.Find("input[aria-label='EtherNet/IP address']").Change("Motor_Temp");

        // The choice is untouched (pinned at the model level by
        // TryApplyDatatypeSuggestion_OperatorAlreadyChose_LeavesChoiceUntouched),
        // so the wizard makes no claim about where the value came from.
        cut.FindAll(SuggestedNote).Should().BeEmpty();
        cut.FindAll(NotInferredNote).Should().BeEmpty();
    }
}
