// ============================================================================
// File: BrotherHttpDemoApi.cs
// Purpose: Synthetic Brother CNC emulator implementing IBrotherHttpApi.
//          Used when EDGECONNECT_BROTHER_FAKE_MODE=true so Studio /
//          dashboards / MQTT pipelines can be demoed without a real
//          Brother CNC. Pure managed C# — no HttpClient, no network IO.
//
// Locked profile (M.P2.4 v3.1 §C.2 — state evolution):
//   * Five scenarios rotate every 12 seconds: running → idle → alarm →
//     standby → maintenance → (back to running). 60-second full cycle.
//   * Parts counter increments by 1 each time the "running" scenario
//     completes a phase boundary.
//   * Cycle time jitters ±10% around a per-scenario baseline (deterministic
//     across the cycle index so test assertions stay reproducible).
//   * Maintenance notice DuePercent drifts upward by 0.1% per cycle.
//   * Alarm transitions happen on cycle boundaries (no mid-phase flicker).
//
// Endpoint response shapes preserved from the legacy adapter's documented
// formats (BrotherHttpDataSource.cs header + per-parser doc comments) so
// the parity oracle (LegacyCanonicalMapper, step 8) can consume identical
// bytes from both backends.
// Reference: docs/sessions/2026-05-21-mp24-brother-http-plan-v3.md §6 catalog,
//            v3.1 §C.2 demo state evolution
// ============================================================================

using System;
using System.Globalization;

namespace ElpisEdgeConnect.Sources.BrotherHttp;

/// <summary>
/// Synthetic Brother HTTP CNC emulator. Constructed by the adapter's
/// production path when <see cref="BrotherHttpDemoModeOptions.IsEnabled"/>
/// is true.
/// </summary>
internal sealed class BrotherHttpDemoApi : IBrotherHttpApi
{
    // ── Cycle constants (seconds) ─────────────────────────────────────────
    private const double PhaseSeconds = 12.0;
    private const int ScenarioCount = 5;
    private const double CycleSeconds = PhaseSeconds * ScenarioCount; // 60s

    // ── Demo profile ──────────────────────────────────────────────────────
    private const string Hostname = "BRN68E74A6608EA";
    private const string Model = "SXd1";
    private const long BasePartsCount = 994;
    private const double BaseCycleTimeSeconds = 296.2;
    private const double CycleTimeJitterFraction = 0.10;
    private const double MaintenanceDriftPercentPerCycle = 0.1;
    private const double MaintenanceBaselinePercent = 87.0;

    private readonly Func<DateTime> _clock;
    private readonly DateTime _startedAt;

    /// <summary>Production constructor — uses <see cref="DateTime.UtcNow"/> as the clock.</summary>
    public BrotherHttpDemoApi()
        : this(() => DateTime.UtcNow)
    {
    }

    /// <summary>
    /// Test constructor — injectable clock so tests can advance state
    /// deterministically without <see cref="System.Threading.Thread.Sleep(int)"/>.
    /// </summary>
    internal BrotherHttpDemoApi(Func<DateTime> clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _startedAt = _clock();
    }

    // ─── Time + state helpers ─────────────────────────────────────────────

    private TimeSpan Elapsed => _clock() - _startedAt;

    private int CurrentCycle => (int)(Elapsed.TotalSeconds / CycleSeconds);

    private DemoScenario CurrentScenario
    {
        get
        {
            var phaseIndex = (int)((Elapsed.TotalSeconds % CycleSeconds) / PhaseSeconds);
            if (phaseIndex < 0) phaseIndex = 0;
            if (phaseIndex >= ScenarioCount) phaseIndex = ScenarioCount - 1;
            return (DemoScenario)phaseIndex;
        }
    }

    /// <summary>
    /// Test-only accessor exposing the live scenario so deterministic-clock
    /// tests can pin scenario rotation without reading parser output.
    /// </summary>
    internal DemoScenario CurrentScenarioForTesting => CurrentScenario;

    private long CurrentPartsCount =>
        BasePartsCount + (CurrentScenario >= DemoScenario.Idle ? CurrentCycle + 1 : CurrentCycle);

    private double CurrentCycleTimeSeconds
    {
        get
        {
            // Deterministic ±10% jitter keyed off the cycle index — same
            // cycle always yields the same value (test reproducibility).
            var jitter = ((CurrentCycle % 7) - 3) / 3.0 * CycleTimeJitterFraction;
            return BaseCycleTimeSeconds * (1.0 + jitter);
        }
    }

    private double CurrentMaintenanceDuePercent =>
        MaintenanceBaselinePercent + (CurrentCycle * MaintenanceDriftPercentPerCycle);

    // ─── IBrotherHttpApi implementations ──────────────────────────────────

    public Task<string?> GetMachineInfoAsync(CancellationToken ct) =>
        Task.FromResult<string?>(BuildMachineInfo(CurrentScenario));

    public Task<string?> GetCycleTimeAsync(CancellationToken ct) =>
        Task.FromResult<string?>(BuildCycleTime(CurrentScenario));

    public Task<string?> GetWorkCountersAsync(CancellationToken ct) =>
        Task.FromResult<string?>(BuildWorkCounters(CurrentScenario));

    public Task<string?> GetAtcToolsAsync(CancellationToken ct) =>
        Task.FromResult<string?>(BuildAtcTools());

    public Task<string?> GetAlarmsAsync(CancellationToken ct) =>
        Task.FromResult<string?>(BuildAlarms(CurrentScenario));

    public Task<string?> GetMaintenanceNoticesAsync(CancellationToken ct) =>
        Task.FromResult<string?>(BuildMaintenanceNotices(CurrentScenario));

    // ─── Response builders (preserve legacy wire shapes) ──────────────────

    private static string BuildMachineInfo(DemoScenario scenario)
    {
        // Format: hostname,model,statusCode,?,?,language
        // Status codes: 0=Off, 1=Idle, 2=Hold, 3=Running, 4=Alarm/Notice, 5=Standby
        var status = scenario switch
        {
            DemoScenario.Running => 3,
            DemoScenario.Idle => 1,
            DemoScenario.Alarm => 4,
            DemoScenario.Standby => 5,
            DemoScenario.Maintenance => 1,
            _ => 1,
        };
        return $"{Hostname},{Model},{status},01,0,1";
    }

    private string BuildCycleTime(DemoScenario scenario)
    {
        var cycleSeconds = CurrentCycleTimeSeconds;
        var hours = (int)(cycleSeconds / 3600);
        var minutes = (int)((cycleSeconds % 3600) / 60);
        var secs = cycleSeconds % 60;
        var cycleTime = $"{hours:D4}:{minutes:D2}:{secs.ToString("00.0", CultureInfo.InvariantCulture)}";

        var program = scenario == DemoScenario.Running ? "0926" : "0000";

        // Multi-line key,value format from legacy ParseCycleTime header.
        return string.Join('\n',
            $"Program,(,{program},)",
            $"Cycle time,{cycleTime}",
            "Cutting time,0000:04:43.5",
            "Non cutting time,0000:00:12.7",
            "Others,0.0",
            "Cutting time / cycle time, 95 %",
            "(Other times are not included in the cycle time)",
            "Operation time,3462:45:28",
            "Power on time,2496:17:52",
            "Total operation time,3465:37:02",
            "Date,20260322115936",
            $"Operation end counter,{CurrentPartsCount:D4}");
    }

    private string BuildWorkCounters(DemoScenario _)
    {
        // 4 counter lines, format: count, current, target, endSignal, ?, ?
        return string.Join('\n',
            $"{CurrentPartsCount},     0,     0,     0,,0",
            "    0,     0,     0,     0,,0",
            "    0,     0,     0,     0,,0",
            "    0,     0,     0,     0,,0");
    }

    private static string BuildAtcTools()
    {
        // ATC magazine snapshot — static across scenarios (a real demo
        // could rotate active tool, but step-3 keeps this stable so the
        // parity test focuses on scenario-driven state).
        // Format per legacy ParseAtcTools comments.
        return string.Join('\n',
            "Program(0926)",
            "01,,,001,,,              ,,, 448.254x0.000,,,,,,********,,,STD tool,,,,#a0a0a4,#a0a0a4,0",
            "11,#000000,#ff8000,011,,,DRILL6.35     ,,, 537.900x10.100,,,,,,********,,,STD tool,,,,#a0a0a4,#a0a0a4,0",
            "12,,,012,,,FACEMILL      ,,, 612.500x25.000,,,,,,********,,,STD tool,,,,#a0a0a4,#a0a0a4,0");
    }

    private static string BuildAlarms(DemoScenario scenario)
    {
        // Empty body = no alarms. Lines: alarmNo, message, program, ?, statusCode
        return scenario switch
        {
            DemoScenario.Alarm => "0512, *Servo overheat,0926,001186,4",
            DemoScenario.Standby => "0501, *Standby mode,0926,001186,3",
            _ => string.Empty,
        };
    }

    private string BuildMaintenanceNotices(DemoScenario scenario)
    {
        if (scenario != DemoScenario.Maintenance)
        {
            return "None";
        }

        var current = (long)(100_000 * (CurrentMaintenanceDuePercent / 100.0));
        const long limit = 100_000;

        // Format per legacy ParseMaintenanceNotices: condition, description,
        // status, current, limit, notified, state, color
        return string.Join('\n',
            $"Z-axis travel distance),GREASING XYZ AXIS   ,Valid,{current,9},{limit,9},Notified,Warning,#ff8000");
    }
}

/// <summary>
/// Discrete scenarios cycled by <see cref="BrotherHttpDemoApi"/>.
/// Public so tests can reference scenario values as method parameters
/// (xUnit Theory test methods must be public, which transitively
/// requires their parameter types to be public).
/// </summary>
public enum DemoScenario
{
    /// <summary>Cycle active, status code 3, program O0926 running.</summary>
    Running = 0,

    /// <summary>Machine idle, no active program, status code 1, no alarms.</summary>
    Idle = 1,

    /// <summary>Real alarm active (servo overheat), status code 4.</summary>
    Alarm = 2,

    /// <summary>Informational alarm 0501 (Standby mode), status code 5.</summary>
    Standby = 3,

    /// <summary>Maintenance notice notified — tests the §6.2 Status/Warning precedence.</summary>
    Maintenance = 4,
}
