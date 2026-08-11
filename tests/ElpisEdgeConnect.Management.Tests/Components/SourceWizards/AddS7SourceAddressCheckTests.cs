// ============================================================================
// Tests: AddS7Source live address check — the debounced call to
// /api/v1/sources/validate/s7-address that derives the datatype from the
// address.
//
// The success path is the reference behaviour the other wizards are aligned
// to: it auto-fills the datatype and shows the "Address suggests: …" caption.
// The failure path used to be a bare `catch` that emptied the row's validation
// state — leaving an operator with a blank datatype, no hint, no error and no
// log line, indistinguishable from a genuine protocol bug. These tests pin
// both, plus the distinction between a genuine failure and a check that was
// merely superseded by the next keystroke (normal operation — silent).
//
// Rendered in Edit mode so the wizard skips the Add-mode /api/v1/config fetch.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Components.Pages.SourceWizards;
using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests.Components.SourceWizards;

public sealed class AddS7SourceAddressCheckTests : TestContext
{
    private const string AddressCheckUnavailableTestId = "[data-testid='s7-address-check-unavailable']";
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly ScriptedAddressHandler _handler = new();
    private readonly CapturingLoggerProvider _logs = new();

    public AddS7SourceAddressCheckTests()
    {
        Services.AddMudServices();
        Services.AddLogging(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddProvider(_logs);
        });
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") });
    }

    // ── Fakes ───────────────────────────────────────────────────────────

    // Scripted responder for /api/v1/sources/validate/s7-address. Each call
    // pulls the next scripted behaviour (the last one repeats), so a test can
    // say "fail, then succeed" without any timing assumptions.
    private sealed class ScriptedAddressHandler : HttpMessageHandler
    {
        private readonly List<Func<CancellationToken, Task<HttpResponseMessage>>> _script = new();
        private int _index = -1;

        public int Calls { get; private set; }

        public SemaphoreSlim CallStarted { get; } = new(0);

        /// <summary>Completes when a blocked call is actually cancelled by its caller.</summary>
        public TaskCompletionSource InFlightCallCancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Script(params Func<CancellationToken, Task<HttpResponseMessage>>[] steps)
        {
            _script.Clear();
            _script.AddRange(steps);
            _index = -1;
        }

        public static Func<CancellationToken, Task<HttpResponseMessage>> Ok(string json) =>
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        public static Func<CancellationToken, Task<HttpResponseMessage>> Throws() =>
            _ => throw new HttpRequestException("Connection refused (127.0.0.1:5080).");

        // Never completes on its own — only when the caller's token is
        // cancelled, which is exactly what a superseded in-flight check does.
        public Func<CancellationToken, Task<HttpResponseMessage>> BlocksUntilCancelled() =>
            async ct =>
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    InFlightCallCancelled.TrySetResult();
                    throw;
                }

                throw new InvalidOperationException("unreachable");
            };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (!path.EndsWith("/validate/s7-address", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json"),
                });
            }

            Calls++;
            var step = _script[Math.Min(++_index, _script.Count - 1)];
            CallStarted.Release();
            return step(cancellationToken);
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Capturing(this);

        public void Dispose() { }

        private sealed class Capturing : ILogger
        {
            private readonly CapturingLoggerProvider _owner;

            public Capturing(CapturingLoggerProvider owner) => _owner = owner;

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => _owner.Entries.Enqueue((logLevel, formatter(state, exception), exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    // ── Fixtures ────────────────────────────────────────────────────────

    private const string ValidWordJson = """
    {
      "isValid": true,
      "normalizedAddress": "DB1.DBW0",
      "area": "DataBlock",
      "dbNumber": 1,
      "byteOffset": 0,
      "widthHint": "Word",
      "suggestedDatatypes": ["Int", "Word"]
    }
    """;

    // A tagless S7 source, so the test can add a blank row the way an operator
    // does and watch the datatype fill itself in from the address.
    private static SourceInstanceConfig TaglessS7Config()
        => new S7SourceWizardModel { InstanceId = "s7-1", DeviceName = "Hob line", Host = "10.0.0.9" }
            .BuildSourceInstance();

    // A complete, save-able S7 source — used where the point of the test is
    // that a failed CHECK changes nothing about the row's own validity.
    private static SourceInstanceConfig CompleteS7Config()
    {
        var model = new S7SourceWizardModel { InstanceId = "s7-1", DeviceName = "Hob line", Host = "10.0.0.9" };
        model.Tags.Add(new S7TagWizardRow
        {
            Name = "cycles",
            Address = "DB1.DBW0",
            Datatype = "Int",
            ScanRateMs = 1000,
        });
        return model.BuildSourceInstance();
    }

    private IRenderedFragment RenderEdit(SourceInstanceConfig cfg) => Render(builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<AddS7Source>(1);
        builder.AddAttribute(2, "EditMode", EditModeContext.Edit(cfg.InstanceId, "v1"));
        builder.AddAttribute(3, "HydratedConfig", cfg);
        builder.CloseComponent();
    });

    private IRenderedFragment RenderEditWithOneBlankTag()
    {
        var cut = RenderEdit(TaglessS7Config());
        cut.Find("[data-testid='s7-add-first-tag']").Click();
        return cut;
    }

    private IRenderedFragment RenderEditWithOneCompleteTag() => RenderEdit(CompleteS7Config());

    // Types into the address cell the way a browser does — an input event
    // while typing, then a change event — so the field's 350 ms debounce
    // starts and eventually fires the wizard's address check.
    private static void TypeAddress(IRenderedFragment cut, string address)
    {
        cut.Find("input[aria-label='S7 address']").Input(address);
        cut.Find("input[aria-label='S7 address']").Change(address);
    }

    private IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> WarningsOrWorse()
        => _logs.Entries.Where(e => e.Level >= LogLevel.Warning).ToList();

    // ── Success path (the reference behaviour — must not regress) ───────

    [Fact]
    public void OnAddressChanged_WhenCheckSucceeds_AutoFillsDatatypeAndShowsSuggestionCaption()
    {
        _handler.Script(ScriptedAddressHandler.Ok(ValidWordJson));
        var cut = RenderEditWithOneBlankTag();

        TypeAddress(cut, "DB1.DBW0");

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Address suggests: Int or Word");
            cut.Markup.Should().Contain("DataBlock · Word · DB1.DBW0");
        }, WaitTimeout);
        // Auto-filled from the first suggestion, and no failure notice.
        cut.FindAll("input[aria-label='Datatype']").Single()
            .GetAttribute("value").Should().Be("Int");
        cut.FindAll(AddressCheckUnavailableTestId).Should().BeEmpty();
    }

    // ── Failure path (the fix) ─────────────────────────────────────────

    [Fact]
    public void OnAddressChanged_WhenCheckFails_ShowsInlineNoticeAndLogsWarning()
    {
        _handler.Script(ScriptedAddressHandler.Throws());
        var cut = RenderEditWithOneBlankTag();

        TypeAddress(cut, "DB1.DBW0");

        cut.WaitForAssertion(
            () => cut.FindAll(AddressCheckUnavailableTestId).Should().NotBeEmpty(),
            WaitTimeout);
        cut.Markup.Should().Contain("Couldn't check this address — enter the datatype manually.");
    }

    [Fact]
    public void OnAddressChanged_WhenCheckFails_LogsTheAddressAndTheException()
    {
        _handler.Script(ScriptedAddressHandler.Throws());
        var cut = RenderEditWithOneBlankTag();

        TypeAddress(cut, "DB1.DBW0");

        cut.WaitForAssertion(() => WarningsOrWorse().Should().NotBeEmpty(), WaitTimeout);
        var entry = WarningsOrWorse().Single();
        entry.Message.Should().Contain("DB1.DBW0");
        entry.Exception.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public void OnAddressChanged_WhenCheckFails_DoesNotBlockSaveOrFlagTheOperatorsInput()
    {
        _handler.Script(ScriptedAddressHandler.Throws());
        var cut = RenderEditWithOneCompleteTag();

        TypeAddress(cut, "DB1.DBD4");

        cut.WaitForAssertion(
            () => cut.FindAll(AddressCheckUnavailableTestId).Should().NotBeEmpty(),
            WaitTimeout);
        // The operator did nothing wrong — only the check failed. So the row's
        // input is not flagged and Save stays available.
        var addressInput = cut.Find("input[aria-label='S7 address']");
        addressInput.GetAttribute("aria-invalid").Should().NotBe("true");
        addressInput.ParentElement!.ClassName.Should().NotContain("mud-input-error");
        cut.Find("[data-testid='wizard-actions-save']").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void OnAddressChanged_WhenALaterCheckSucceeds_ClearsTheInlineNotice()
    {
        _handler.Script(ScriptedAddressHandler.Throws(), ScriptedAddressHandler.Ok(ValidWordJson));
        var cut = RenderEditWithOneBlankTag();

        TypeAddress(cut, "DB1.DBW1");
        cut.WaitForAssertion(
            () => cut.FindAll(AddressCheckUnavailableTestId).Should().NotBeEmpty(),
            WaitTimeout);

        TypeAddress(cut, "DB1.DBW0");

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(AddressCheckUnavailableTestId).Should().BeEmpty();
            cut.Markup.Should().Contain("Address suggests: Int or Word");
        }, WaitTimeout);
    }

    // ── Cancellation is normal operation, not failure ──────────────────

    [Fact]
    public async Task OnAddressChanged_WhenSupersededByANewerKeystroke_ShowsNoNoticeAndLogsNothing()
    {
        // First check hangs; the second keystroke supersedes it.
        _handler.Script(_handler.BlocksUntilCancelled(), ScriptedAddressHandler.Ok(ValidWordJson));
        var cut = RenderEditWithOneBlankTag();

        TypeAddress(cut, "DB1.DBW");
        (await _handler.CallStarted.WaitAsync(WaitTimeout)).Should().BeTrue("the first check must be in flight");

        TypeAddress(cut, "DB1.DBW0");

        // The newer keystroke cancels the stale call, so a late answer can
        // never land on top of a newer one.
        await _handler.InFlightCallCancelled.Task.WaitAsync(WaitTimeout);
        cut.WaitForAssertion(
            () => cut.Markup.Should().Contain("Address suggests: Int or Word"),
            WaitTimeout);
        // That cancellation is ordinary operation, not a failure: no caption,
        // no log line.
        cut.FindAll(AddressCheckUnavailableTestId).Should().BeEmpty();
        WarningsOrWorse().Should().BeEmpty();
        _handler.Calls.Should().Be(2);
    }
}
