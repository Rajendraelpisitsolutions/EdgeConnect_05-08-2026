// ============================================================================
// Tests: LayoutChromeModel — pins the top-of-page chrome decisions for
//        MainLayout.razor. The Razor is a thin shell over this POCO so
//        unit tests give bUnit-equivalent coverage of the banner-visibility
//        rule and pin the locked copy strings.
// Reference: docs/sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v3.md §1.4
// ============================================================================

using ElpisEdgeConnect.Management.Components.Shared;
using ElpisEdgeConnect.Management.Options;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class LayoutChromeModelTests
{
    [Fact]
    public void Focas2FakeModeBanner_IsVisible_WhenOptionsFlagTrue()
    {
        var options = new ManagementOptions { Focas2FakeMode = true };

        var model = new LayoutChromeModel(options);

        model.Focas2FakeModeBannerVisible.Should().BeTrue();
    }

    [Fact]
    public void Focas2FakeModeBanner_IsHidden_WhenOptionsFlagFalse()
    {
        var options = new ManagementOptions { Focas2FakeMode = false };

        var model = new LayoutChromeModel(options);

        model.Focas2FakeModeBannerVisible.Should().BeFalse();
    }

    [Fact]
    public void BannerCopy_IsLocked_AsOperatorFacingText()
    {
        // Pin the visible copy so accidental wording changes are caught by
        // CI rather than discovered in a sales demo.
        LayoutChromeModel.Focas2FakeModeBannerTitle.Should().Be("Demo mode active.");
        LayoutChromeModel.Focas2FakeModeBannerMessage.Should()
            .Contain("synthetic controller", "operator must know data is not real")
            .And.Contain("EDGECONNECT_FOCAS2_FAKE_MODE", "operator must know how to disable")
            .And.Contain("restart", "operator must know toggle requires restart");
    }

    [Fact]
    public void S7FakeModeBanner_IsVisible_WhenOptionsFlagTrue()
    {
        var model = new LayoutChromeModel(new ManagementOptions { S7FakeMode = true });
        model.S7FakeModeBannerVisible.Should().BeTrue();
    }

    [Fact]
    public void S7FakeModeBanner_IsHidden_WhenOptionsFlagFalse()
    {
        var model = new LayoutChromeModel(new ManagementOptions { S7FakeMode = false });
        model.S7FakeModeBannerVisible.Should().BeFalse();
    }

    [Fact]
    public void S7BannerCopy_IsLocked_AsOperatorFacingText()
    {
        LayoutChromeModel.S7FakeModeBannerTitle.Should().Be("Demo mode active.");
        LayoutChromeModel.S7FakeModeBannerMessage.Should()
            .Contain("synthetic PLC", "operator must know data is not real")
            .And.Contain("EDGECONNECT_S7_FAKE_MODE", "operator must know how to disable")
            .And.Contain("restart", "operator must know toggle requires restart");
    }
}
