// ============================================================================
// File: LicenseTrialState.cs
// Purpose: Shared, thread-safe view of the unlicensed demo countdown so the
//          management/UI layer can show a "demo mode — data stops in HH:MM:SS"
//          banner and reflect when data collection has been stopped.
//          Written by LicenseTrialEnforcer; read by the status API.
//
//          The demo budget (default 2h) is consumed ONLY while a source is
//          actively collecting data — the countdown pauses when nothing is
//          connected/reading. So `Remaining` here is the demo time LEFT, not a
//          wall-clock deadline, and `Counting` says whether it is currently
//          ticking down.
// Reference: docs/decisions/0035-unlicensed-runtime-cutoff.md
// ============================================================================

using System;

namespace ElpisEdgeConnect.Host;

/// <summary>
/// Observable state of the unlicensed demo window (ADR-0035). Singleton. The
/// budget is spent only while data is actively collected (see
/// <see cref="LicenseTrialEnforcer"/>).
/// </summary>
public sealed class LicenseTrialState
{
    private readonly object _lock = new();
    private TimeSpan? _remaining;
    private bool _counting;
    private bool _dataStopped;
    private bool _versionRestricted;
    private bool _clockTampered;

    /// <summary>
    /// Publish the demo budget left and whether it is currently being consumed.
    /// <paramref name="remaining"/> is <c>null</c> when the license is valid
    /// (no demo countdown). <paramref name="counting"/> is true when a source is
    /// actively collecting data this instant (so the countdown is advancing).
    /// </summary>
    public void SetDemo(TimeSpan? remaining, bool counting)
    {
        lock (_lock)
        {
            _remaining = remaining;
            _counting = counting;
        }
    }

    /// <summary>Mark that the demo budget was exhausted and data collection was stopped.</summary>
    public void MarkDataStopped()
    {
        lock (_lock)
        {
            _dataStopped = true;
            _counting = false;
        }
    }

    /// <summary>True once the demo budget elapsed and reading/publishing was stopped.</summary>
    public bool DataStopped
    {
        get { lock (_lock) { return _dataStopped; } }
    }

    /// <summary>
    /// Mark that data was stopped because this build is a newer version than the
    /// (expired) license covers (version restriction).
    /// </summary>
    public void MarkVersionRestricted()
    {
        lock (_lock)
        {
            _versionRestricted = true;
            _dataStopped = true;
            _counting = false;
        }
    }

    /// <summary>True when data is stopped due to a version restriction (newer build than licensed).</summary>
    public bool VersionRestricted
    {
        get { lock (_lock) { return _versionRestricted; } }
    }

    /// <summary>
    /// Record whether the system clock currently appears to have been wound back
    /// (tampered). Set by the enforcer when it detects the clock is behind the
    /// highest time already observed; cleared once the clock is corrected.
    /// </summary>
    public void SetClockTampered(bool tampered)
    {
        lock (_lock)
        {
            _clockTampered = tampered;
        }
    }

    /// <summary>
    /// True when the system date/time appears to have been rolled back — the
    /// license is not honoured and the runtime is in demo mode until the clock
    /// is corrected.
    /// </summary>
    public bool ClockTampered
    {
        get { lock (_lock) { return _clockTampered; } }
    }

    /// <summary>
    /// True when the demo budget is actively counting down right now (i.e. a
    /// source is collecting data). False when paused (nothing collecting),
    /// licensed, or already stopped.
    /// </summary>
    public bool Counting
    {
        get { lock (_lock) { return _counting && !_dataStopped; } }
    }

    /// <summary>
    /// Demo time remaining; <c>null</c> when licensed (no countdown),
    /// <see cref="TimeSpan.Zero"/> once the budget is spent / data stopped.
    /// This is the budget LEFT, and only shrinks while <see cref="Counting"/>.
    /// </summary>
    public TimeSpan? Remaining()
    {
        lock (_lock)
        {
            if (_dataStopped)
            {
                return TimeSpan.Zero;
            }
            return _remaining;
        }
    }
}
