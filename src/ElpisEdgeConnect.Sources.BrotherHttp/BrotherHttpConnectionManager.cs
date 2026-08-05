// ============================================================================
// File: BrotherHttpConnectionManager.cs
// Purpose: Owns the per-instance "have we ever talked to this Brother CNC"
//          flag plus the consecutive-failure counter that drives the
//          Connecting → Connected → Faulted lifecycle per Q4.
//          HTTPD_MCNINFO is the sole health authority — failures on the
//          other five endpoints degrade data fidelity but never fault the
//          adapter.
// Reference: docs/sessions/2026-05-21-mp24-brother-http-plan-v3.md §3 (Q4),
//            v3.1 §B.5 (per-endpoint failure metric — recorded by the
//            adapter, not here; this class is pure lifecycle state).
// ============================================================================

using System;
using Microsoft.Extensions.Logging;

namespace ElpisEdgeConnect.Sources.BrotherHttp;

/// <summary>
/// Tracks Brother CNC health based on <c>HTTPD_MCNINFO</c> outcomes.
/// Stateful per adapter instance; not thread-safe (the adapter's
/// single-flight guard per v3.1 §B.3 serialises access).
/// </summary>
internal sealed class BrotherHttpConnectionManager
{
    private readonly int _faultThreshold;
    private readonly string _instanceId;
    private readonly ILogger _logger;

    private int _consecutiveFailures;
    private bool _everConnected;

    public BrotherHttpConnectionManager(int faultThreshold, string instanceId, ILogger logger)
    {
        if (faultThreshold < 1)
        {
            throw new ArgumentException(
                $"FaultThreshold must be >= 1; got {faultThreshold}.",
                nameof(faultThreshold));
        }
        _faultThreshold = faultThreshold;
        _instanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>True once the adapter has had at least one successful HTTPD_MCNINFO round-trip.</summary>
    public bool EverConnected => _everConnected;

    /// <summary>Current consecutive HTTPD_MCNINFO failure count.</summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>True when consecutive failures meet/exceed the configured fault threshold.</summary>
    public bool ShouldFault => _consecutiveFailures >= _faultThreshold;

    /// <summary>Record a successful <c>HTTPD_MCNINFO</c> call. Resets the failure counter.</summary>
    public void RecordHealthSuccess()
    {
        if (!_everConnected)
        {
            _logger.LogInformation(
                "Brother source '{InstanceId}' established first successful connection.",
                _instanceId);
        }
        else if (_consecutiveFailures > 0)
        {
            _logger.LogInformation(
                "Brother source '{InstanceId}' recovered after {Failures} consecutive failures.",
                _instanceId, _consecutiveFailures);
        }
        _everConnected = true;
        _consecutiveFailures = 0;
    }

    /// <summary>Record a failed <c>HTTPD_MCNINFO</c> call. Increments the consecutive-failure counter.</summary>
    public void RecordHealthFailure()
    {
        _consecutiveFailures++;
        _logger.LogWarning(
            "Brother source '{InstanceId}' health check failed (consecutive: {N}; threshold: {Threshold}).",
            _instanceId, _consecutiveFailures, _faultThreshold);
    }

    /// <summary>Reset state for an adapter Restart. Clears both flags.</summary>
    public void Reset()
    {
        _consecutiveFailures = 0;
        _everConnected = false;
    }
}
