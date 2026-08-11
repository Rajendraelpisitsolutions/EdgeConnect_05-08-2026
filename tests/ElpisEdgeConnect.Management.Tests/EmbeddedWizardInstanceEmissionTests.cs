// ============================================================================
// File: EmbeddedWizardInstanceEmissionTests.cs
// Purpose: Reproduces and guards the onboarding "details not saved" defect.
//
//          REPORTED SYMPTOM
//          In the onboarding flow an operator picks FOCAS2, types the IP
//          192.168.1.101, saves, completes the destination and route steps,
//          presses Save changes — and re-opening the source shows an IP of
//          "1".
//
//          ROOT CAUSE
//          An embedded wizard hands its built instance to the meta-wizard
//          through OnInstanceBuilt, fired from OnAfterRenderAsync. That
//          method used to return early whenever CanSave() had not FLIPPED
//          since the last emission:
//
//              if (_lastNotifiedValid == isValid) return;
//
//          The operator types "192.168.1.101" one character at a time. The
//          form first satisfies CanSave() at the very first character, so the
//          parent is handed an instance carrying ipAddress = "1". Every later
//          keystroke leaves validity unchanged at true, so the guard suppresses
//          re-emission and the parent keeps the stale first value — which is
//          what the bundled apply then writes to gateway.json.
//
//          ADR-0016 Rule 4 explicitly allows this callback to "fire repeatedly
//          while the operator types; no debounce in the wizard", so the guard
//          contradicted the decision it cited. It cannot simply be deleted,
//          though: the meta-wizard calls StateHasChanged() on receipt, which
//          re-renders the child, which would emit again — an endless loop. The
//          fix dedupes on the CONTENT of the built instance instead.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Components.Pages.SourceWizards;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class EmbeddedWizardInstanceEmissionTests : TestContext
{
    public EmbeddedWizardInstanceEmissionTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(new ElpisEdgeConnect.Management.Options.ManagementOptions());
        Services.AddSingleton(new HttpClient(new EmptyConfigHandler())
        {
            BaseAddress = new Uri("http://localhost"),
        });
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public async Task EmbeddedFocas2_IpTypedCharacterByCharacter_EmitsTheCompleteAddress()
    {
        var emitted = new List<SourceInstanceConfig?>();

        var cut = RenderWizard(emitted);

        cut.WaitForState(() => cut.FindAll("input").Count > 0);

        SetField(cut, "#field-instance-id", "FocasTest1");

        // The operator types the address; each keystroke is its own input event.
        const string address = "192.168.1.101";
        for (var i = 1; i <= address.Length; i++)
        {
            SetIpField(cut, address[..i]);
        }

        await Task.Yield();

        var last = LastNonNull(emitted);
        last.Should().NotBeNull("the form is valid once a complete address is typed");
        IpOf(last!).Should().Be(
            "192.168.1.101",
            "the parent must receive the address the operator finished typing, not the prefix that first satisfied CanSave()");
    }

    [Fact]
    public async Task EmbeddedFocas2_AddressExtendedAfterAlreadyValid_EmitsTheExtendedAddress()
    {
        // The sharpest case: "192.168.1.10" is already a complete, valid IPv4
        // address, so validity does NOT flip when the final "1" is typed. A
        // validity-flip guard silently keeps 192.168.1.10 — a different machine.
        var emitted = new List<SourceInstanceConfig?>();

        var cut = RenderWizard(emitted);

        cut.WaitForState(() => cut.FindAll("input").Count > 0);

        SetField(cut, "#field-instance-id", "FocasTest1");
        SetIpField(cut, "192.168.1.10");
        SetIpField(cut, "192.168.1.101");

        await Task.Yield();

        IpOf(LastNonNull(emitted)!).Should().Be("192.168.1.101");
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    /// <summary>
    /// Renders the wizard alongside MudBlazor's popover provider, which the
    /// select controls require, and collects everything it emits.
    /// </summary>
    private IRenderedFragment RenderWizard(List<SourceInstanceConfig?> emitted)
        => Render(builder =>
        {
            builder.OpenComponent<MudBlazor.MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<AddFocas2Source>(1);
            builder.AddAttribute(2, nameof(AddFocas2Source.EmbeddedMode), true);
            builder.AddAttribute(3, nameof(AddFocas2Source.OnInstanceBuilt),
                EventCallback.Factory.Create<SourceInstanceConfig?>(this, i => emitted.Add(i)));
            builder.CloseComponent();
        });

    private static SourceInstanceConfig? LastNonNull(List<SourceInstanceConfig?> emitted)
    {
        for (var i = emitted.Count - 1; i >= 0; i--)
        {
            if (emitted[i] is not null) return emitted[i];
        }
        return null;
    }

    private static string? IpOf(SourceInstanceConfig instance)
        => instance.Connection is { } c && c.TryGetProperty("ipAddress", out var ip)
            ? ip.GetString()
            : null;

    private static void SetIpField(IRenderedFragment cut, string value)
        => SetField(cut, "#field-ip-address", value);

    private static void SetField(IRenderedFragment cut, string selector, string value)
    {
        var el = cut.FindAll(selector);
        if (el.Count == 0)
        {
            throw new InvalidOperationException($"Field '{selector}' not found.");
        }
        el[0].Input(value);
    }

    /// <summary>Returns an empty gateway configuration for the wizard's init GET.</summary>
    private sealed class EmptyConfigHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(new GatewayConfiguration
            {
                Gateway = new GatewaySettings
                {
                    GatewayId = "gw-test",
                    GatewayName = "Test gateway",
                },
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
