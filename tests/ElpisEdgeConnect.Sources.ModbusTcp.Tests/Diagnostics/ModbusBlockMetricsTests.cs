// ============================================================================
// File: Diagnostics/ModbusBlockMetricsTests.cs
// Purpose: Unit tests for ModbusBlockMetrics — counters, RTT statistics
//          (Welford mean, min/max/p95/latest), failure classification,
//          timestamps, and snapshot semantics.
//
// ModbusBlockMetrics is internal — the test project sees it via
// InternalsVisibleTo on the source project's csproj.
// ============================================================================

using System;
using System.Collections.Generic;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Sources.ModbusTcp.Diagnostics;
using ElpisEdgeConnect.Sources.ModbusTcp.Scanning;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sources.ModbusTcp.Tests.Diagnostics;

public sealed class ModbusBlockMetricsTests
{
    private static readonly ScanBlockKey TestKey = new(
        UnitId: 1,
        RegisterClass: ModbusRegisterClass.HoldingRegister,
        StartAddress: 0,
        Count: 10);

    private static ModbusBlockMetrics NewMetrics() => new(TestKey);

    // -------------------------------------------------------------
    // Empty state
    // -------------------------------------------------------------

    [Fact]
    public void Snapshot_Empty_ReturnsZeroesAndNullRtt()
    {
        var s = NewMetrics().Snapshot();

        s.Key.Should().Be(TestKey);
        s.Transactions.Should().Be(0);
        s.Successes.Should().Be(0);
        s.Failures.Should().Be(0);
        s.TransportErrors.Should().Be(0);
        s.SlaveExceptions.Should().Be(0);
        s.DecodeErrors.Should().Be(0);
        s.RttMeanMs.Should().BeNull();
        s.RttMinMs.Should().BeNull();
        s.RttMaxMs.Should().BeNull();
        s.RttP95Ms.Should().BeNull();
        s.RttLatestMs.Should().BeNull();
        s.LastSuccessAt.Should().BeNull();
        s.LastFailureAt.Should().BeNull();
        s.LastErrorCode.Should().BeNull();
    }

    // -------------------------------------------------------------
    // Successful transactions — RTT stats
    // -------------------------------------------------------------

    [Fact]
    public void Record_Successes_UpdatesCountersAndRttStats()
    {
        var m = NewMetrics();

        m.RecordTransaction(true, TimeSpan.FromMilliseconds(5), retryCount: 0,
            TransactionFailureKind.None, null, null);
        m.RecordTransaction(true, TimeSpan.FromMilliseconds(10), retryCount: 0,
            TransactionFailureKind.None, null, null);
        m.RecordTransaction(true, TimeSpan.FromMilliseconds(15), retryCount: 0,
            TransactionFailureKind.None, null, null);

        var s = m.Snapshot();
        s.Transactions.Should().Be(3);
        s.Successes.Should().Be(3);
        s.Failures.Should().Be(0);
        s.RttMeanMs.Should().BeApproximately(10.0, 1e-9);
        s.RttMinMs.Should().Be(5.0);
        s.RttMaxMs.Should().Be(15.0);
        s.RttLatestMs.Should().Be(15.0);
        s.LastSuccessAt.Should().NotBeNull();
    }

    [Fact]
    public void Record_WelfordMean_StableOverManySamples()
    {
        // A naive `sum/count` would drift on long poll histories; Welford
        // gives the exact running mean within floating-point precision.
        var m = NewMetrics();
        for (var i = 1; i <= 1000; i++)
        {
            m.RecordTransaction(true, TimeSpan.FromMilliseconds(i),
                retryCount: 0, TransactionFailureKind.None, null, null);
        }
        // Mean of 1..1000 = 500.5
        m.Snapshot().RttMeanMs.Should().BeApproximately(500.5, 1e-6);
    }

    [Fact]
    public void Record_Latest_IsTheMostRecentSample()
    {
        var m = NewMetrics();
        m.RecordTransaction(true, TimeSpan.FromMilliseconds(100), 0, TransactionFailureKind.None, null, null);
        m.RecordTransaction(true, TimeSpan.FromMilliseconds(200), 0, TransactionFailureKind.None, null, null);
        m.RecordTransaction(true, TimeSpan.FromMilliseconds(50), 0, TransactionFailureKind.None, null, null);

        m.Snapshot().RttLatestMs.Should().Be(50.0);
    }

    [Fact]
    public void Record_P95_OnKnownDistribution()
    {
        var m = NewMetrics();
        // 100 samples linearly from 1..100 ms. p95 by nearest-rank = sample at ceil(0.95 * 100) = index 94 → 95ms.
        for (var i = 1; i <= 100; i++)
        {
            m.RecordTransaction(true, TimeSpan.FromMilliseconds(i), 0, TransactionFailureKind.None, null, null);
        }

        var s = m.Snapshot();
        s.RttP95Ms.Should().Be(95.0);
    }

    [Fact]
    public void Record_P95_RingBufferRetainsOnlyRecentSamples()
    {
        // First 500 samples are tiny; last 100 are large. The ring holds 100,
        // so p95 should reflect the recent (large) population.
        var m = NewMetrics();
        for (var i = 0; i < 500; i++)
        {
            m.RecordTransaction(true, TimeSpan.FromMilliseconds(1), 0, TransactionFailureKind.None, null, null);
        }
        for (var i = 1; i <= 100; i++)
        {
            m.RecordTransaction(true, TimeSpan.FromMilliseconds(1000 + i), 0, TransactionFailureKind.None, null, null);
        }

        var s = m.Snapshot();
        s.RttP95Ms.Should().BeGreaterThan(1090.0, "ring holds only the recent high-latency samples");
    }

    // -------------------------------------------------------------
    // Failure classification
    // -------------------------------------------------------------

    [Fact]
    public void Record_TransportFailure_IncrementsTransportCounter()
    {
        var m = NewMetrics();

        m.RecordTransaction(false, TimeSpan.FromMilliseconds(200), retryCount: 2,
            TransactionFailureKind.Transport,
            errorCode: ModbusErrors.Timeout,
            errorCategory: ErrorCategory.Network);

        var s = m.Snapshot();
        s.Transactions.Should().Be(1);
        s.Failures.Should().Be(1);
        s.TransportErrors.Should().Be(1);
        s.SlaveExceptions.Should().Be(0);
        s.DecodeErrors.Should().Be(0);
        s.Retries.Should().Be(2);
        s.LastErrorCode.Should().Be(ModbusErrors.Timeout);
        s.LastErrorCategory.Should().Be("Network");
    }

    [Fact]
    public void Record_SlaveException_IncrementsSlaveExceptionCounter()
    {
        var m = NewMetrics();

        m.RecordTransaction(false, TimeSpan.FromMilliseconds(15), retryCount: 0,
            TransactionFailureKind.SlaveException,
            errorCode: ModbusErrors.SlaveException,
            errorCategory: ErrorCategory.Protocol);

        var s = m.Snapshot();
        s.Failures.Should().Be(1);
        s.SlaveExceptions.Should().Be(1);
        s.TransportErrors.Should().Be(0);
    }

    [Fact]
    public void Record_DecodeError_IncrementsDecodeCounterWithoutCountingAsFailure()
    {
        // Decode errors happen POST-executor-success. The per-block Failures
        // counter should stay zero even though the tag was lost.
        var m = NewMetrics();

        m.RecordDecodeError(errorCode: "MODBUS.DECODE_FAILED", errorCategory: ErrorCategory.Protocol);

        var s = m.Snapshot();
        s.Failures.Should().Be(0);
        s.DecodeErrors.Should().Be(1);
        s.LastFailureAt.Should().NotBeNull("decode errors still bump the last-failure timestamp");
    }

    [Fact]
    public void Record_FailureThenSuccess_BothTimestampsPopulated()
    {
        var m = NewMetrics();
        m.RecordTransaction(false, TimeSpan.FromMilliseconds(200), 0,
            TransactionFailureKind.Transport, ModbusErrors.Timeout, ErrorCategory.Network);
        m.RecordTransaction(true, TimeSpan.FromMilliseconds(10), 0,
            TransactionFailureKind.None, null, null);

        var s = m.Snapshot();
        s.LastFailureAt.Should().NotBeNull();
        s.LastSuccessAt.Should().NotBeNull();
        s.LastSuccessAt.Should().BeOnOrAfter(s.LastFailureAt!.Value);
    }

    [Fact]
    public void Record_FailuresDoNotPollute_RttStats()
    {
        // A failed transaction that took 5 seconds (with retries) must not
        // push the RTT max to 5000ms. Only successes feed RTT.
        var m = NewMetrics();

        m.RecordTransaction(true, TimeSpan.FromMilliseconds(10), 0, TransactionFailureKind.None, null, null);
        m.RecordTransaction(false, TimeSpan.FromMilliseconds(5000), 3,
            TransactionFailureKind.Transport, ModbusErrors.Timeout, ErrorCategory.Network);
        m.RecordTransaction(true, TimeSpan.FromMilliseconds(12), 0, TransactionFailureKind.None, null, null);

        var s = m.Snapshot();
        s.RttMaxMs.Should().Be(12.0);
        s.RttMeanMs.Should().BeApproximately(11.0, 1e-9);
    }

    [Fact]
    public void Record_RetryCount_AccumulatesAcrossTransactions()
    {
        var m = NewMetrics();
        m.RecordTransaction(true, TimeSpan.FromMilliseconds(5), retryCount: 1,
            TransactionFailureKind.None, null, null);
        m.RecordTransaction(false, TimeSpan.FromMilliseconds(200), retryCount: 3,
            TransactionFailureKind.Transport, ModbusErrors.Timeout, ErrorCategory.Network);
        m.RecordTransaction(true, TimeSpan.FromMilliseconds(7), retryCount: 0,
            TransactionFailureKind.None, null, null);

        m.Snapshot().Retries.Should().Be(4);
    }
}
