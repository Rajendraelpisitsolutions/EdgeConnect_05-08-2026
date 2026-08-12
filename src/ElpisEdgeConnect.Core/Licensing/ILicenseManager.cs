// ============================================================================
// File: Licensing/ILicenseManager.cs
// Purpose: Public contract for the license manager. Loads a signed license
//          file, exposes a snapshot, and provides fast-path checks for
//          adapter registration and config validation.
// Reference: PHASE1_EXECUTION_PLAN.md Milestone B3, ARCHITECTURE_BLUEPRINT.md §7
// ============================================================================

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ElpisEdgeConnect.Core.Licensing;

/// <summary>
/// Loads and exposes the signed license file. All read APIs are lock-free
/// and may be called from any thread; loads are serialized internally.
/// </summary>
public interface ILicenseManager
{
    /// <summary>Currently loaded license, or <c>null</c> if none has been loaded.</summary>
    LicenseInfo? Current { get; }

    /// <summary>Lifecycle status of the currently loaded license.</summary>
    LicenseStatus Status { get; }

    /// <summary>
    /// Time remaining in the grace period when <see cref="Status"/> is
    /// <see cref="LicenseStatus.InGracePeriod"/>; <c>null</c> otherwise.
    /// </summary>
    TimeSpan? RemainingGrace { get; }

    /// <summary>Raised whenever an expiration boundary is crossed.</summary>
    event EventHandler<LicenseWarning>? WarningRaised;

    /// <summary>Load and verify a license from a file path.</summary>
    Task LoadFromFileAsync(string path, CancellationToken cancellationToken);

    /// <summary>Load and verify a license from an open stream.</summary>
    Task LoadAsync(Stream licenseJson, CancellationToken cancellationToken);

    /// <summary>
    /// Fast-path check: is the named module enabled by the current license?
    /// Returns <c>false</c> when no license is loaded.
    /// </summary>
    bool IsModuleEnabled(string moduleKey);

    /// <summary>
    /// Fast-path check: is the named feature enabled by the current license?
    /// Returns <c>false</c> when no license is loaded.
    /// </summary>
    bool IsFeatureEnabled(string featureKey);

    /// <summary>
    /// Check whether configuring <paramref name="proposedCount"/> instances
    /// of <paramref name="moduleKey"/> would exceed the per-module limit.
    /// </summary>
    LicenseEvaluationResult CheckInstanceLimit(string moduleKey, int proposedCount);

    /// <summary>
    /// Re-evaluate the loaded license against the current wall clock and
    /// raise warning events if a boundary has been crossed since the last
    /// evaluation. Intended to be called from a background tick by the host;
    /// safe to call frequently because warnings are only raised once per
    /// boundary.
    /// </summary>
    void Tick();

    /// <summary>
    /// Discard the in-memory license, reverting to
    /// <see cref="LicenseStatus.NotLoaded"/> (i.e. unlicensed / demo mode).
    /// Called by the host when the license file is deleted, replaced with an
    /// unreadable file, or otherwise fails to re-verify — so that removing the
    /// license file takes effect immediately instead of only on the next
    /// restart. Idempotent.
    /// </summary>
    void Unload();
}
