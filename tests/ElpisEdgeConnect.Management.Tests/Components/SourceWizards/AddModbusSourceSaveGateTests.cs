// ============================================================================
// Tests: AddModbusSource save-gate — which connection field CanSave() requires
// per encapsulation, and (second half of the file) the EmbeddedMode contract
// the onboarding meta-wizard depends on.
//
// Regression: CanSave() required Host unconditionally, but ADR-0033's SerialRtu
// mode addresses the slave by serial port and never renders a Host field. That
// left the Save button permanently disabled for every serial Modbus RTU source
// — and, because EmbeddedMode only emits OnInstanceBuilt when CanSave() is
// true, it also pinned the onboarding flow's Next button off at step 3.
//
// The Edit-mode cases below pin the CanSave() branch directly (Edit skips the
// Add-mode /api/v1/config fetch). The EmbeddedMode cases walk the whole
// onboarding chain end to end — OnInitializedAsync's RtuMode seeding →
// CanSave() → BuildSourceInstance() → OnAfterRenderAsync's content-signature
// emit → the SourceInstanceConfig OnboardingFlow.CanGoNext() reads at step 3.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Components.Pages.SourceWizards;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests.Components.SourceWizards;

public sealed class AddModbusSourceSaveGateTests : TestContext
{
    public AddModbusSourceSaveGateTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new HttpClient(new EmptyConfigHandler())
        {
            BaseAddress = new Uri("http://localhost"),
        });
    }

    // Add mode (which EmbeddedMode always is) fetches GET /api/v1/config in
    // OnInitializedAsync; a failure there parks WizardShell on its load-error
    // branch and no form fields render at all. Serve an empty-but-valid gateway
    // config: no existing sources, so no instance-id collision can mask the
    // behaviour under test (this mirrors the dev gateway, whose Sources list is
    // empty — the id "Rtu" the operator reported collides with nothing).
    private sealed class EmptyConfigHandler : HttpMessageHandler
    {
        internal const string ConfigJson = """
        {
          "gateway": { "gatewayId": "dev-gateway", "gatewayName": "Dev gateway" },
          "sources": [],
          "sinks": [],
          "routes": []
        }
        """;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ConfigJson, Encoding.UTF8, "application/json"),
            });
    }

    private static SourceInstanceConfig SerialRtuConfig(string serialPort = "/dev/ttyUSB0") =>
        new ModbusSourceWizardModel
        {
            InstanceId = "rtu-line-3",
            DeviceId = "dev3",
            DeviceClass = "plc",
            ProtocolName = "modbusrtu",
            Encapsulation = "SerialRtu",
            SerialPort = serialPort,
            BaudRate = 19200,
            Parity = "Even",
            StopBits = "One",
            Handshake = "None",
        }.BuildSourceInstance();

    private static SourceInstanceConfig TcpConfig(string host = "192.168.1.42") =>
        new ModbusSourceWizardModel
        {
            InstanceId = "tcp-1",
            DeviceId = "dev1",
            ProtocolName = "modbustcp",
            Encapsulation = "Tcp",
            Host = host,
        }.BuildSourceInstance();

    // Render the wizard in Edit mode wrapped in a MudPopoverProvider so the
    // MudSelect / MudTooltip instances have their required provider in the tree.
    private IRenderedFragment RenderEdit(SourceInstanceConfig cfg) => Render(builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<AddModbusSource>(1);
        builder.AddAttribute(2, "EditMode", EditModeContext.Edit(cfg.InstanceId, "v1"));
        builder.AddAttribute(3, "HydratedConfig", cfg);
        builder.CloseComponent();
    });

    private static bool SaveIsEnabled(IRenderedFragment cut) =>
        !cut.Find("[data-testid='wizard-actions-save']").HasAttribute("disabled");

    [Fact]
    public void SerialRtu_source_with_a_serial_port_enables_Save()
    {
        var cut = RenderEdit(SerialRtuConfig());

        SaveIsEnabled(cut).Should().BeTrue(
            "SerialRtu addresses the slave by serial port, so a blank Host must not gate Save");
    }

    [Fact]
    public void SerialRtu_source_without_a_serial_port_keeps_Save_disabled()
    {
        var cut = RenderEdit(SerialRtuConfig(serialPort: string.Empty));

        SaveIsEnabled(cut).Should().BeFalse(
            "SerialPort is the required connection field for SerialRtu");
    }

    [Fact]
    public void Tcp_source_with_a_host_enables_Save()
    {
        var cut = RenderEdit(TcpConfig());

        SaveIsEnabled(cut).Should().BeTrue();
    }

    [Fact]
    public void Tcp_source_without_a_host_keeps_Save_disabled()
    {
        var cut = RenderEdit(TcpConfig(host: string.Empty));

        SaveIsEnabled(cut).Should().BeFalse("Host is the required connection field for Tcp");
    }

    // ── EmbeddedMode: the onboarding "Connect a device" path ────────────────
    //
    // OnboardingFlow mounts <AddModbusSource EmbeddedMode="true" RtuMode="true"
    // OnInstanceBuilt="@OnSourceInstanceBuilt" /> at step 3 and gates Next on
    // `_sourceInstance is not null && _sourceIdCollision is null`. The wizard
    // only ever emits a non-null instance from OnAfterRenderAsync when
    // CanSave() is true, so these cases prove the fixed CanSave() actually
    // reaches the parent for a serial-RTU model.

    private const string InstanceIdInput = "input#field-instance-id, #field-instance-id input";
    private const string SerialPortInput =
        "input[data-testid='modbus-serial-port'], [data-testid='modbus-serial-port'] input";
    // The tag table's first cell is the Name MudTextField (see the tag row in
    // AddModbusSource.razor); Modbus is the only source wizard with a per-row
    // items table, so this is unambiguous within the rendered wizard.
    private const string FirstTagNameInput = "tbody tr td:first-child input";

    /// <summary>
    /// Render the wizard exactly as OnboardingFlow does for the "modbus-rtu"
    /// tile, capturing every value the OnInstanceBuilt callback receives. The
    /// last captured value is what OnboardingFlow's <c>_sourceInstance</c>
    /// holds, i.e. what CanGoNext() reads at step 3.
    /// </summary>
    private (IRenderedFragment Cut, List<SourceInstanceConfig?> Emitted) RenderEmbeddedRtu()
    {
        var emitted = new List<SourceInstanceConfig?>();
        var cut = Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<AddModbusSource>(1);
            builder.AddAttribute(2, "EmbeddedMode", true);
            builder.AddAttribute(3, "RtuMode", true);
            builder.AddAttribute(4, "OnInstanceBuilt", EventCallback.Factory.Create<SourceInstanceConfig?>(
                this, (SourceInstanceConfig? instance) => emitted.Add(instance)));
            builder.CloseComponent();
        });
        return (cut, emitted);
    }

    /// <summary>
    /// Type into a MudBlazor text field. MudBlazor commits the typed value on
    /// <c>oninput</c> when <c>Immediate="true"</c> (instance id, serial port)
    /// and on <c>onchange</c> otherwise (the tag-row cells), so fire both and
    /// let the field honour whichever one it wires.
    /// </summary>
    private static void TypeInto(IRenderedFragment cut, string selector, string value)
    {
        // WizardShell parks on a progress bar until the Add-mode
        // GET /api/v1/config completes, so wait for the field rather than
        // racing the load.
        cut.WaitForAssertion(
            () => cut.FindAll(selector).Should().NotBeEmpty(
                "the wizard must render '{0}' for the operator to fill in", selector),
            TimeSpan.FromSeconds(5));

        TryTrigger(() => cut.FindAll(selector)[0].Input(value));
        TryTrigger(() => cut.FindAll(selector)[0].Change(value));
    }

    private static void TryTrigger(Action trigger)
    {
        try
        {
            trigger();
        }
        catch (MissingEventHandlerException)
        {
            // This field wires the other of the oninput/onchange pair.
        }
    }

    private static void ClickAddTag(IRenderedFragment cut)
        => cut.FindAll("button")
              .First(b => b.TextContent.Contains("Add tag", StringComparison.Ordinal))
              .Click();

    private static SourceInstanceConfig? LastEmitted(List<SourceInstanceConfig?> emitted)
        => emitted.Count == 0 ? null : emitted[^1];

    [Fact]
    public void Embedded_rtu_emits_a_built_instance_once_instance_id_and_serial_port_are_filled()
    {
        var (cut, emitted) = RenderEmbeddedRtu();

        TypeInto(cut, InstanceIdInput, "Rtu");
        TypeInto(cut, SerialPortInput, "COM3");

        cut.WaitForAssertion(
            () => LastEmitted(emitted).Should().NotBeNull(
                "OnboardingFlow.CanGoNext() enables Next at step 3 only when the wizard " +
                "has emitted a non-null SourceInstanceConfig"),
            TimeSpan.FromSeconds(5));

        var built = LastEmitted(emitted)!;
        built.InstanceId.Should().Be("Rtu");
        built.ProtocolName.Should().Be("modbusrtu",
            "RtuMode seeds the modbusrtu protocol id in OnInitializedAsync");

        var connection = built.Connection!.Value;
        connection.GetProperty("encapsulation").GetString().Should().Be("SerialRtu");
        connection.GetProperty("serialPort").GetString().Should().Be("COM3");
        connection.GetProperty("baudRate").GetInt32().Should().Be(9600,
            "the default baud rate already satisfies CanSave()'s BaudRate > 0 gate");
    }

    [Fact]
    public void Embedded_rtu_without_a_serial_port_emits_nothing_buildable()
    {
        var (cut, emitted) = RenderEmbeddedRtu();

        TypeInto(cut, InstanceIdInput, "Rtu");

        // The wizard emits on first render regardless, so there IS a value —
        // it just has to still be null while the required RTU connection field
        // is blank. This is the legitimate "Next stays greyed out" state.
        cut.WaitForAssertion(() => emitted.Should().NotBeEmpty(), TimeSpan.FromSeconds(5));
        LastEmitted(emitted).Should().BeNull(
            "SerialPort is the required connection field for SerialRtu, so Next must stay disabled");
    }

    [Fact]
    public void Embedded_rtu_with_a_named_tag_emits_the_tag_in_the_built_instance()
    {
        var (cut, emitted) = RenderEmbeddedRtu();

        TypeInto(cut, InstanceIdInput, "Rtu");
        TypeInto(cut, SerialPortInput, "COM3");
        ClickAddTag(cut);
        TypeInto(cut, FirstTagNameInput, "spindle_load");

        cut.WaitForAssertion(
            () => LastEmitted(emitted).Should().NotBeNull(),
            TimeSpan.FromSeconds(5));

        var tags = LastEmitted(emitted)!.Connection!.Value.GetProperty("tagDefinitions");
        tags.GetArrayLength().Should().Be(1);
        tags[0].GetProperty("name").GetString().Should().Be("spindle_load");
        tags[0].GetProperty("registerClass").GetString().Should().Be("HoldingRegister");
    }

    [Fact]
    public void Embedded_rtu_with_an_unnamed_tag_row_emits_nothing_buildable()
    {
        var (cut, emitted) = RenderEmbeddedRtu();

        TypeInto(cut, InstanceIdInput, "Rtu");
        TypeInto(cut, SerialPortInput, "COM3");

        cut.WaitForAssertion(
            () => LastEmitted(emitted).Should().NotBeNull(),
            TimeSpan.FromSeconds(5));

        ClickAddTag(cut);

        // ModbusTagValidator requires a tag name; CanSave() rejects any row with
        // issues, so an added-but-unnamed row takes Next back off. The wizard's
        // validation banner explains why ("Tag 'row 1': Tag name is required.").
        cut.WaitForAssertion(
            () => LastEmitted(emitted).Should().BeNull(),
            TimeSpan.FromSeconds(5));
    }
}
