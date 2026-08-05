// ============================================================================
// File: PermissiveLicenseManager.cs
// Purpose: ILicenseManager substitute used by the soak runner to bypass the
//          production file-based license check. Always reports Valid + all
//          modules enabled — appropriate for sim / dev / soak runs only.
//
//          Pattern mirrors HostHarness.cs (the integration-test harness)
//          which uses NSubstitute to do the same thing.
// ============================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Licensing;

namespace ElpisEdgeConnect.Tools.ModbusSoakRunner;

/// <summary>
/// All-permissive ILicenseManager. Returns <see cref="LicenseStatus.Valid"/>
/// and treats every module / feature as enabled. NEVER use in production —
/// the soak runner is a dev / sim tool only.
/// </summary>
internal sealed class PermissiveLicenseManager : ILicenseManager
{
    public LicenseInfo? Current => null;

    public LicenseStatus Status => LicenseStatus.Valid;

    public TimeSpan? RemainingGrace => null;

    public event EventHandler<LicenseWarning>? WarningRaised
    {
        // Suppressed — no warnings ever fire from a permissive license.
        add { _ = value; }
        remove { _ = value; }
    }

    public Task LoadFromFileAsync(string path, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task LoadAsync(Stream licenseJson, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public bool IsModuleEnabled(string moduleKey) => true;

    public bool IsFeatureEnabled(string featureKey) => true;

    public LicenseEvaluationResult CheckInstanceLimit(string moduleKey, int proposedCount)
        => new() { Allowed = true };

    /// <summary>No-op — there's nothing to expire and no warnings to raise.</summary>
    public void Tick() { /* permissive: nothing to monitor */ }

    /// <summary>No-op — a permissive license is always Valid and never unloads.</summary>
    public void Unload() { /* permissive: stays Valid */ }
}
