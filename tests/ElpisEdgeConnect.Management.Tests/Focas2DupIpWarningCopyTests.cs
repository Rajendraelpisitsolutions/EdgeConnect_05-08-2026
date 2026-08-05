// ============================================================================
// Tests: Focas2DupIpWarningCopy — pins both copy variants for Q10 of the
//        M.2b.3.1 review:
//          * production: warns about a second real handle
//          * demo mode:  explains the duplicate is harmless
//        The wizard reads these strings live from the helper; tests catch
//        accidental UX wording drift.
// Reference: docs/sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v2.md §3 Q10
// ============================================================================

using ElpisEdgeConnect.Management.Wizards;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public sealed class Focas2DupIpWarningCopyTests
{
    [Fact]
    public void ProductionMode_Copy_WarnsAboutSecondRealHandle()
    {
        var copy = Focas2DupIpWarningCopy.For("192.168.1.10:8193", fakeMode: false);

        copy.Should().Contain("192.168.1.10:8193", "endpoint must appear verbatim");
        copy.Should().Contain("second handle",
            "production copy must flag the operational risk of two handles to one controller");
        copy.Should().Contain("mistake",
            "production copy must indicate the configuration is usually wrong");
    }

    [Fact]
    public void DemoMode_Copy_ExplainsSharedSimulatedController()
    {
        var copy = Focas2DupIpWarningCopy.For("10.0.0.5:8193", fakeMode: true);

        copy.Should().Contain("10.0.0.5:8193", "endpoint must appear verbatim");
        copy.Should().Contain("simulated controller",
            "demo copy must reassure the operator that the duplicate is intentional/harmless");
        copy.Should().Contain("harmless",
            "demo copy must explicitly call the situation harmless");
        copy.Should().Contain("In production",
            "demo copy must remind operators what this would mean outside demo mode");
    }
}
