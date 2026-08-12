// ============================================================================
// File: Components/Shared/LayoutChromeModel.cs
// Purpose: View-model for the Studio's top-of-page chrome decisions. Pulled
//          out of MainLayout.razor so the Razor stays a thin shell over a
//          unit-testable POCO (mirrors ReloadOutcomePanelModel + the
//          SourceProtocolPickerModel pattern; the project does not use bUnit).
//
//          The first decision driven by this model is the FOCAS2 demo-mode
//          banner — visible iff ManagementOptions.Focas2FakeMode is true.
//          Future banner-or-chrome decisions (license warnings, manifest
//          drift, etc.) can land here without touching MainLayout's structure.
// Reference: docs/sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v3.md §1.4
// ============================================================================

using ElpisEdgeConnect.Management.Options;

namespace ElpisEdgeConnect.Management.Components.Shared;

/// <summary>
/// Read-only view-model for top-of-page chrome decisions in
/// <c>MainLayout.razor</c>. Constructed per render from the live
/// <see cref="ManagementOptions"/>.
/// </summary>
public sealed class LayoutChromeModel
{
    /// <summary>Title text rendered as the strong-prefix of the demo-mode banner.</summary>
    public const string Focas2FakeModeBannerTitle = "Demo mode active.";

    /// <summary>Body text rendered after the banner title.</summary>
    public const string Focas2FakeModeBannerMessage =
        "FOCAS2 sources are running against a synthetic controller — no real CNC connections. " +
        "To disable: clear EDGECONNECT_FOCAS2_FAKE_MODE and restart EdgeConnect.";

    /// <summary>Title text rendered as the strong-prefix of the S7 demo-mode banner.</summary>
    public const string S7FakeModeBannerTitle = "Demo mode active.";

    /// <summary>Body text rendered after the S7 banner title.</summary>
    public const string S7FakeModeBannerMessage =
        "Siemens S7 sources are running against a synthetic PLC — no real connections. " +
        "To disable: clear EDGECONNECT_S7_FAKE_MODE and restart EdgeConnect.";

    /// <summary>Construct the model from the live management options.</summary>
    public LayoutChromeModel(ManagementOptions options)
    {
        System.ArgumentNullException.ThrowIfNull(options);
        Focas2FakeModeBannerVisible = options.Focas2FakeMode;
        S7FakeModeBannerVisible = options.S7FakeMode;
    }

    /// <summary>
    /// True when the Studio should render the sticky amber FOCAS2 demo-mode
    /// banner. Derived from <see cref="ManagementOptions.Focas2FakeMode"/>.
    /// </summary>
    public bool Focas2FakeModeBannerVisible { get; }

    /// <summary>
    /// True when the Studio should render the sticky amber S7 demo-mode
    /// banner. Derived from <see cref="ManagementOptions.S7FakeMode"/>.
    /// </summary>
    public bool S7FakeModeBannerVisible { get; }
}
