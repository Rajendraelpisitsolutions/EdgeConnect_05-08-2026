// ============================================================================
// File: Wizards/Focas2DupIpWarningCopy.cs
// Purpose: Operator-facing copy for the FOCAS2 wizard's dup-IP:Port warning.
//          Two variants pinned by Q10 of the M.2b.3.1 review:
//             * production: warns about a second real handle to the same controller
//             * demo mode:  explains that the duplicate is harmless because both
//                           sources share the same synthetic controller
//          Pulled out of AddFocas2Source.razor so unit tests can pin the copy.
// Reference: docs/sessions/2026-05-18-mp2b31-focas2-demo-mode-plan-v2.md §3 Q10
// ============================================================================

namespace ElpisEdgeConnect.Management.Wizards;

/// <summary>
/// Pure helper that produces the right dup-IP:Port warning copy for the
/// FOCAS2 wizard based on whether demo mode is active.
/// </summary>
public static class Focas2DupIpWarningCopy
{
    /// <summary>
    /// Compose the warning shown when the operator types an IP:Port that
    /// matches an existing FOCAS2 source.
    /// </summary>
    /// <param name="endpoint">The colliding <c>IpAddress:Port</c> string.</param>
    /// <param name="fakeMode">True when <c>EDGECONNECT_FOCAS2_FAKE_MODE</c> is active.</param>
    public static string For(string endpoint, bool fakeMode) =>
        fakeMode
            ? $"Two FOCAS2 sources point at {endpoint}. In demo mode, both are backed by " +
              "the same simulated controller (this is harmless). In production, this would " +
              "mean two real handles to the same controller."
            : $"Another FOCAS2 source already targets {endpoint}. This will open a second " +
              "handle to the same controller — usually a mistake outside of test/commissioning rigs.";
}
