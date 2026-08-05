// ============================================================================
// Tests: ChooseSourceProtocol render — the MELSEC tile shows Available when
// source-melsec is licensed (or no license loaded = permissive) and a locked
// "Requires Premium" state when the module is disabled. UI is explanatory only;
// the backend gate stays authoritative.
// ============================================================================

using System;
using System.Collections.Frozen;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using ElpisEdgeConnect.Core.Licensing;
using ElpisEdgeConnect.Management.Components.Pages.SourceWizards;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests.Components.SourceWizards;

public sealed class ChooseSourceProtocolTests : TestContext
{
    public ChooseSourceProtocolTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private sealed class FakeLicenseManager : ILicenseManager
    {
        private readonly bool _loaded;
        private readonly Func<string, bool> _enabled;
        public FakeLicenseManager(bool loaded, Func<string, bool> enabled) { _loaded = loaded; _enabled = enabled; }

        public LicenseInfo? Current => _loaded ? Sample() : null;
        public LicenseStatus Status => default;
        public TimeSpan? RemainingGrace => null;
        public Task LoadFromFileAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LoadAsync(Stream licenseJson, CancellationToken cancellationToken) => Task.CompletedTask;
        public bool IsModuleEnabled(string moduleKey) => _enabled(moduleKey);
        public bool IsFeatureEnabled(string featureKey) => true;
        public LicenseEvaluationResult CheckInstanceLimit(string moduleKey, int proposedCount) => throw new NotSupportedException();
        public void Tick() { }
        public void Unload() { }
        public event EventHandler<LicenseWarning>? WarningRaised { add { } remove { } }

        private static LicenseInfo Sample() => new()
        {
            LicenseId = "T",
            Customer = "T",
            GatewayId = "gw",
            Edition = LicenseEdition.Professional,
            IssuedAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            Limits = new LicenseLimits { MaxSourceInstances = 100, MaxSinkInstances = 100, MaxRoutes = 100 },
            Modules = FrozenDictionary<string, LicenseModule>.Empty,
        };
    }

    // Render the picker with a MudPopoverProvider so MudTooltip (used by the
    // locked-tile branch) has its required provider in the tree.
    private IRenderedFragment RenderPicker() => Render(builder =>
    {
        builder.OpenComponent<MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<ChooseSourceProtocol>(1);
        builder.CloseComponent();
    });

    [Fact]
    public void MelsecTile_IsAvailable_WhenNoLicenseLoaded()
    {
        // No ILicenseManager registered -> permissive gate -> Available.
        var cut = RenderPicker();

        cut.Markup.Should().Contain("Mitsubishi MELSEC");
        cut.FindAll("[data-testid='tile-requires-license']").Should().BeEmpty();
    }

    [Fact]
    public void MelsecTile_IsRequiresPremium_WhenModuleDisabled()
    {
        Services.AddSingleton<ILicenseManager>(new FakeLicenseManager(loaded: true, enabled: key => key != "source-melsec"));

        var cut = RenderPicker();

        cut.FindAll("[data-testid='tile-requires-license']").Should().NotBeEmpty();
        cut.Markup.Should().Contain("Requires Premium");
        cut.Markup.Should().Contain("Mitsubishi MELSEC");
    }

    [Fact]
    public void MelsecTile_IsAvailable_WhenModuleEnabled()
    {
        Services.AddSingleton<ILicenseManager>(new FakeLicenseManager(loaded: true, enabled: _ => true));

        var cut = RenderPicker();

        cut.FindAll("[data-testid='tile-requires-license']").Should().BeEmpty();
        cut.Markup.Should().Contain("Mitsubishi MELSEC");
    }
}
