// ============================================================================
// File: Mapping/SparkplugTimestampTests.cs
// Purpose: Locks the frozen timestamp range rules (plan v2 §3.3): exact epoch,
//          epoch+1ms, sub-millisecond flooring, pre-epoch rejection, and
//          offset normalization per the strict UTC contract.
// ============================================================================

using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Sinks.SparkplugB;
using ElpisEdgeConnect.Sinks.SparkplugB.Mapping;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Mapping;

public sealed class SparkplugTimestampTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    [Fact]
    public void ToUnixMilliseconds_ExactEpoch_ReturnsZero()
    {
        SparkplugTimestamp.ToUnixMilliseconds(Epoch).Should().Be(0UL);
    }

    [Fact]
    public void ToUnixMilliseconds_OneMillisecondAfterEpoch_ReturnsOne()
    {
        SparkplugTimestamp.ToUnixMilliseconds(Epoch.AddMilliseconds(1)).Should().Be(1UL);
    }

    [Fact]
    public void ToUnixMilliseconds_SubMillisecondPrecision_IsFloored()
    {
        // 1.7 ms after epoch = 17,000 ticks; whole milliseconds = 1 (floored, not rounded).
        var instant = Epoch.AddTicks(17_000);

        SparkplugTimestamp.ToUnixMilliseconds(instant).Should().Be(1UL);
    }

    [Fact]
    public void ToUnixMilliseconds_PreEpoch_ThrowsTypedPreEpochError()
    {
        var act = () => SparkplugTimestamp.ToUnixMilliseconds(Epoch.AddTicks(-1));

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.EncodeTimestampPreEpoch);
    }

    [Fact]
    public void ToUnixMilliseconds_NonUtcOffset_IsNormalizedToUtc()
    {
        // 1970-01-01T05:30:00+05:30 IS the epoch instant.
        var instant = new DateTimeOffset(1970, 1, 1, 5, 30, 0, TimeSpan.FromHours(5.5));

        SparkplugTimestamp.ToUnixMilliseconds(instant).Should().Be(0UL);
    }

    [Fact]
    public void ToUnixMilliseconds_MaximumDateTimeOffset_DoesNotOverflow()
    {
        // Year 9999 is far below the ulong millisecond ceiling; overflow is structurally unreachable.
        SparkplugTimestamp.ToUnixMilliseconds(DateTimeOffset.MaxValue).Should().Be(253_402_300_799_999UL);
    }

    [Fact]
    public void ValueToUnixMilliseconds_UtcKind_ConvertsDirectly()
    {
        var value = new DateTime(1970, 1, 1, 0, 0, 1, DateTimeKind.Utc);

        SparkplugTimestamp.ValueToUnixMilliseconds(value).Should().Be(1000UL);
    }

    [Fact]
    public void ValueToUnixMilliseconds_UnspecifiedKind_IsTreatedAsUtc()
    {
        var value = new DateTime(1970, 1, 1, 0, 0, 1, DateTimeKind.Unspecified);

        SparkplugTimestamp.ValueToUnixMilliseconds(value).Should().Be(1000UL);
    }

    [Fact]
    public void ValueToUnixMilliseconds_PreEpochValue_ThrowsTypedPreEpochError()
    {
        var value = new DateTime(1969, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        var act = () => SparkplugTimestamp.ValueToUnixMilliseconds(value);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.EncodeTimestampPreEpoch);
    }
}
