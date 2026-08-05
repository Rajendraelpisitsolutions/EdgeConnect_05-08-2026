// ============================================================================
// File: Payloads/SparkplugPayloadEncoderTests.cs
// Purpose: Slice-4 factory tests — every byte-level assertion goes through the
//          INDEPENDENT wire decoder (never generated parsing). Locks: the
//          corrected alias policy (bdSeq aliased; Node Control/Rebirth
//          name-only), the frozen NBIRTH ordering, alias-only NDATA with
//          chronological ordering, the exact NDEATH field-absence profile,
//          QualityReason absence on null metrics (slice-3 r1 projection-level
//          proof), and every alias-table rejection. Field numbers: Payload
//          timestamp=1 metrics=2 seq=3; Metric name=1 alias=2 timestamp=3
//          datatype=4 is_historical=5 is_null=7 properties=9 int_value=10
//          long_value=11 boolean_value=14; PropertySet keys=1 values=2;
//          PropertyValue type=1 int_value=3 string_value=8.
// ============================================================================

using System.Text;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sinks.SparkplugB;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;
using ElpisEdgeConnect.Sinks.SparkplugB.Payloads;
using ElpisEdgeConnect.Sinks.SparkplugB.Tests.Wire;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Payloads;

public sealed class SparkplugPayloadEncoderTests
{
    private static readonly DateTimeOffset Publication = DateTimeOffset.UnixEpoch.AddSeconds(100);
    private static readonly SparkplugSequenceNumber Seq0 = SparkplugSequenceNumber.Create(0);
    private static readonly SparkplugBirthDeathSequence BdSeq7 = SparkplugBirthDeathSequence.Create(7);
    private const ulong BdSeqAlias = 1;

    private static readonly SparkplugAliasKey KeyA = SparkplugAliasKey.Create("cnc", "m1", "alpha");
    private static readonly SparkplugAliasKey KeyB = SparkplugAliasKey.Create("cnc", "m1", "beta");

    private static readonly IReadOnlyDictionary<SparkplugAliasKey, ulong> AliasMap =
        new Dictionary<SparkplugAliasKey, ulong> { [KeyA] = 2, [KeyB] = 3 };

    private static readonly IReadOnlyDictionary<SparkplugAliasKey, ulong> EmptyAliasMap =
        new Dictionary<SparkplugAliasKey, ulong>();

    private static readonly IReadOnlyDictionary<SparkplugAliasKey, ulong> AliasMapAOnly =
        new Dictionary<SparkplugAliasKey, ulong> { [KeyA] = 2 };

    private static SparkplugMetricSample Sample(SparkplugAliasKey key, object? value = null, bool isNull = false,
        DataQuality quality = DataQuality.Good, double secondsAfterEpoch = 50) => new()
    {
        Key = key,
        ValueType = CanonicalValueType.Integer,
        Value = isNull ? null : value ?? 42,
        IsNull = isNull,
        AcquisitionTimestamp = DateTimeOffset.UnixEpoch.AddSeconds(secondsAfterEpoch),
        Quality = quality,
    };

    // ---- decoding helpers (independent decoder only) ----

    private static List<IReadOnlyList<ProtoWireField>> Metrics(byte[] payload) =>
        ProtoWireDecoder.Decode(payload)
            .Where(f => f.FieldNumber == 2)
            .Select(f => (IReadOnlyList<ProtoWireField>)ProtoWireDecoder.Decode(f.LengthDelimitedBytes))
            .ToList();

    private static ProtoWireField? Field(IReadOnlyList<ProtoWireField> fields, int number) =>
        fields.SingleOrDefault(f => f.FieldNumber == number);

    private static string? Name(IReadOnlyList<ProtoWireField> metric) =>
        Field(metric, 1) is { } f ? Encoding.UTF8.GetString(f.LengthDelimitedBytes) : null;

    // ==== NBIRTH ====

    [Fact]
    public void EncodeNBirth_EmptyRoute_CarriesSeqTimestampBdSeqAndRebirthOnly()
    {
        var bytes = SparkplugPayloadEncoder.EncodeNBirth(Seq0, BdSeq7, BdSeqAlias, Publication, [], EmptyAliasMap);

        var payloadFields = ProtoWireDecoder.Decode(bytes);
        Field(payloadFields, 3).Should().NotBeNull("NBIRTH seq must be physically present even at 0");
        Field(payloadFields, 3)!.VarintValue.Should().Be(0UL);
        Field(payloadFields, 1)!.VarintValue.Should().Be(100_000UL, "the payload timestamp is the publication instant");

        var metrics = Metrics(bytes);
        metrics.Should().HaveCount(2, "an empty route still births bdSeq and Node Control/Rebirth");
        Name(metrics[0]).Should().Be("bdSeq");
        Name(metrics[1]).Should().Be("Node Control/Rebirth");
    }

    [Fact]
    public void EncodeNBirth_BdSeqMetric_HasAliasInt64ValueAndTimestamp()
    {
        var bytes = SparkplugPayloadEncoder.EncodeNBirth(Seq0, BdSeq7, BdSeqAlias, Publication, [], EmptyAliasMap);

        var bdSeq = Metrics(bytes)[0];
        Field(bdSeq, 2)!.VarintValue.Should().Be(BdSeqAlias, "bdSeq HAS an alias (no spec exception exists for it)");
        Field(bdSeq, 4)!.VarintValue.Should().Be(4UL, "bdSeq datatype is Int64");
        Field(bdSeq, 11)!.VarintValue.Should().Be(7UL);
        Field(bdSeq, 3).Should().NotBeNull("NBIRTH metrics carry timestamps");
    }

    [Fact]
    public void EncodeNBirth_RebirthMetric_IsNameOnlyBooleanFalseWithNoAlias()
    {
        var bytes = SparkplugPayloadEncoder.EncodeNBirth(Seq0, BdSeq7, BdSeqAlias, Publication, [], EmptyAliasMap);

        var rebirth = Metrics(bytes)[1];
        Field(rebirth, 2).Should().BeNull("[tck-id-operational-behavior-data-commands-rebirth-name-aliases]: no alias on Node Control/Rebirth");
        Field(rebirth, 4)!.VarintValue.Should().Be(11UL, "Rebirth datatype is Boolean");
        Field(rebirth, 14).Should().NotBeNull("the false value must be physically encoded, not absent");
        Field(rebirth, 14)!.VarintValue.Should().Be(0UL);
    }

    [Fact]
    public void EncodeNBirth_ApplicationMetrics_AreOrderedByMetricNameWithNameAliasAndDatatype()
    {
        var bytes = SparkplugPayloadEncoder.EncodeNBirth(
            Seq0, BdSeq7, BdSeqAlias, Publication, [Sample(KeyB), Sample(KeyA)], AliasMap);

        var metrics = Metrics(bytes);
        metrics.Should().HaveCount(4);
        Name(metrics[2]).Should().Be("cnc/m1/alpha", "application metrics sort by ordinal metric name after the well-known pair");
        Name(metrics[3]).Should().Be("cnc/m1/beta");
        Field(metrics[2], 2)!.VarintValue.Should().Be(2UL);
        Field(metrics[3], 2)!.VarintValue.Should().Be(3UL);
        Field(metrics[2], 4)!.VarintValue.Should().Be(3UL, "canonical Integer maps to Sparkplug Int32");
    }

    [Fact]
    public void EncodeNBirth_NullMetric_CarriesIsNullAndQualityButNeverQualityReasonBytes()
    {
        // The slice-3 r1 projection-level proof: a null metric with a lossy
        // quality has Quality property bytes but NO QualityReason property.
        var bytes = SparkplugPayloadEncoder.EncodeNBirth(
            Seq0, BdSeq7, BdSeqAlias, Publication,
            [Sample(KeyA, isNull: true, quality: DataQuality.Unknown)], AliasMapAOnly);

        var metric = Metrics(bytes)[2];
        Field(metric, 7)!.VarintValue.Should().Be(1UL, "is_null=true must be physically encoded");
        Field(metric, 10).Should().BeNull("a null metric has no value arm");

        var properties = ProtoWireDecoder.Decode(Field(metric, 9)!.LengthDelimitedBytes);
        var keys = properties.Where(f => f.FieldNumber == 1)
            .Select(f => Encoding.UTF8.GetString(f.LengthDelimitedBytes)).ToList();
        keys.Should().Equal(["Quality"], "the frozen contract omits QualityReason for null handling — no property bytes may exist");
    }

    [Fact]
    public void EncodeNBirth_UncertainQuality_CarriesQualityZeroAndControlledReasonInOrder()
    {
        var bytes = SparkplugPayloadEncoder.EncodeNBirth(
            Seq0, BdSeq7, BdSeqAlias, Publication,
            [Sample(KeyA, quality: DataQuality.Uncertain)], AliasMapAOnly);

        var properties = ProtoWireDecoder.Decode(Field(Metrics(bytes)[2], 9)!.LengthDelimitedBytes);
        var keys = properties.Where(f => f.FieldNumber == 1)
            .Select(f => Encoding.UTF8.GetString(f.LengthDelimitedBytes)).ToList();
        keys.Should().Equal(["Quality", "QualityReason"], "deterministic property order");

        var values = properties.Where(f => f.FieldNumber == 2)
            .Select(f => (IReadOnlyList<ProtoWireField>)ProtoWireDecoder.Decode(f.LengthDelimitedBytes)).ToList();
        Field(values[0], 1)!.VarintValue.Should().Be(3UL, "Quality PropertyValue type is Int32 ([tck-id-payloads-propertyset-quality-value-type])");
        Field(values[0], 3)!.VarintValue.Should().Be(0UL, "Uncertain maps to Quality=0; the zero must be physically present");
        Field(values[1], 1)!.VarintValue.Should().Be(12UL, "QualityReason PropertyValue type is String");
        Encoding.UTF8.GetString(Field(values[1], 8)!.LengthDelimitedBytes).Should().Be("quality uncertain");
    }

    [Fact]
    public void EncodeNBirth_GoodQuality_HasNoPropertiesFieldAtAll()
    {
        var bytes = SparkplugPayloadEncoder.EncodeNBirth(
            Seq0, BdSeq7, BdSeqAlias, Publication, [Sample(KeyA)], AliasMapAOnly);

        Field(Metrics(bytes)[2], 9).Should().BeNull("Good omits the Quality property entirely — no PropertySet bytes");
    }

    // ==== NDATA ====

    [Fact]
    public void EncodeNData_Metrics_AreAliasOnlyWithNoNameBytes()
    {
        var bytes = SparkplugPayloadEncoder.EncodeNData(
            Seq0, Publication, [Sample(KeyA)], AliasMap, isHistorical: false);

        var metric = Metrics(bytes).Single();
        Field(metric, 1).Should().BeNull("NDATA metrics carry only the alias; the name MUST be excluded");
        Field(metric, 2)!.VarintValue.Should().Be(2UL);
        Field(metric, 3).Should().NotBeNull("NDATA metrics carry timestamps");
        Field(metric, 5).Should().BeNull("a live batch is not historical");
    }

    [Fact]
    public void EncodeNData_HistoricalBatch_MarksEveryMetricHistorical()
    {
        var bytes = SparkplugPayloadEncoder.EncodeNData(
            Seq0, Publication, [Sample(KeyA), Sample(KeyB)], AliasMap, isHistorical: true);

        foreach (var metric in Metrics(bytes))
        {
            Field(metric, 5)!.VarintValue.Should().Be(1UL, "is_historical=true must be physically encoded on every replayed metric");
        }
    }

    [Fact]
    public void EncodeNData_Samples_AreChronologicallyOrdered()
    {
        var bytes = SparkplugPayloadEncoder.EncodeNData(
            Seq0, Publication,
            [Sample(KeyA, secondsAfterEpoch: 60), Sample(KeyB, secondsAfterEpoch: 40)],
            AliasMap, isHistorical: false);

        var timestamps = Metrics(bytes).Select(m => Field(m, 3)!.VarintValue).ToList();
        timestamps.Should().BeInAscendingOrder("[tck-id-operational-behavior-data-publish-nbirth-order]: chronological order in the metric list");
        Metrics(bytes)[0].Should().Match(m => Field((IReadOnlyList<ProtoWireField>)m, 2)!.VarintValue == 3UL, "the older sample (beta) comes first");
    }

    [Fact]
    public void EncodeNData_SeqZeroAfterWrap_IsPhysicallyPresent()
    {
        var bytes = SparkplugPayloadEncoder.EncodeNData(
            SparkplugSequenceNumber.Create(0), Publication, [Sample(KeyA)], AliasMap, isHistorical: false);

        var seq = ProtoWireDecoder.Decode(bytes).Single(f => f.FieldNumber == 3);
        seq.VarintValue.Should().Be(0UL, "a post-wrap seq=0 must be physically encoded");
    }

    // ==== NDEATH ====

    [Fact]
    public void EncodeNDeath_MatchesTheFrozenFieldPresenceProfileExactly()
    {
        var bytes = SparkplugPayloadEncoder.EncodeNDeath(BdSeq7);

        var payloadFields = ProtoWireDecoder.Decode(bytes);
        payloadFields.Select(f => f.FieldNumber).Should().Equal([2],
            "NDEATH carries no payload timestamp, no seq, no uuid, no body — only the single bdSeq metric");

        var metric = Metrics(bytes).Single();
        metric.Select(f => f.FieldNumber).Should().Equal([1, 4, 11],
            "the NDEATH bdSeq metric is exactly name + datatype + value: no alias, no timestamp, no properties");
        Name(metric).Should().Be("bdSeq");
        Field(metric, 4)!.VarintValue.Should().Be(4UL);
        Field(metric, 11)!.VarintValue.Should().Be(7UL, "the same bdSeq value later used in the paired NBIRTH");
    }

    // ==== NBIRTH alias-baseline set match (slice-4 review r1) ====

    [Fact]
    public void EncodeNBirth_BirthMetricAbsentFromAliasMap_ThrowsAliasTableMismatch()
    {
        var act = () => SparkplugPayloadEncoder.EncodeNBirth(
            Seq0, BdSeq7, BdSeqAlias, Publication, [Sample(KeyA), Sample(KeyB)], AliasMapAOnly);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.AliasTableMismatch);
    }

    [Fact]
    public void EncodeNBirth_AliasMapKeyAbsentFromBirthMetrics_ThrowsAliasTableMismatch()
    {
        var act = () => SparkplugPayloadEncoder.EncodeNBirth(
            Seq0, BdSeq7, BdSeqAlias, Publication, [Sample(KeyA)], AliasMap);

        var error = act.Should().Throw<AdapterException>().Which.Error;
        error.Code.Should().Be(SparkplugErrors.AliasTableMismatch);
        error.Message.Should().Contain("cnc/m1/beta", "the unannounced alias must be identified — surplus aliases are never silently ignored");
    }

    [Fact]
    public void EncodeNBirth_EmptyRouteWithNonEmptyAliasMap_ThrowsAliasTableMismatch()
    {
        var act = () => SparkplugPayloadEncoder.EncodeNBirth(
            Seq0, BdSeq7, BdSeqAlias, Publication, [], AliasMap);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.AliasTableMismatch);
    }

    [Fact]
    public void EncodeNBirth_ExactSetMatch_SucceedsRegardlessOfInputOrdering()
    {
        // Metric list in reverse name order; alias map inserted B-then-A.
        var reversedMap = new Dictionary<SparkplugAliasKey, ulong> { [KeyB] = 3, [KeyA] = 2 };

        var bytes = SparkplugPayloadEncoder.EncodeNBirth(
            Seq0, BdSeq7, BdSeqAlias, Publication, [Sample(KeyB), Sample(KeyA)], reversedMap);

        Metrics(bytes).Should().HaveCount(4, "set comparison is order-insensitive");
    }

    [Fact]
    public void EncodeNBirth_ValidBirth_PhysicallyAnnouncesEveryApplicationAliasWithNameAndDatatype()
    {
        var bytes = SparkplugPayloadEncoder.EncodeNBirth(
            Seq0, BdSeq7, BdSeqAlias, Publication, [Sample(KeyA), Sample(KeyB)], AliasMap);

        var appMetrics = Metrics(bytes).Skip(2).ToList();
        var announced = appMetrics.Select(m => Field(m, 2)!.VarintValue).ToHashSet();
        announced.Should().BeEquivalentTo(AliasMap.Values,
            "every alias later usable by NDATA must be established in the active NBIRTH");
        foreach (var metric in appMetrics)
        {
            Field(metric, 1).Should().NotBeNull("each announced alias carries its metric name");
            Field(metric, 4).Should().NotBeNull("each announced alias carries its datatype");
        }
    }

    // ==== Structural sample validation (slice-4 review r1) ====

    [Fact]
    public void EncodeNBirth_NullListElement_ThrowsArgumentNullException()
    {
        var act = () => SparkplugPayloadEncoder.EncodeNBirth(
            Seq0, BdSeq7, BdSeqAlias, Publication, [null!], EmptyAliasMap);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EncodeNData_SampleWithNullKey_ThrowsTypedIdentityInvalid_NotNullReference()
    {
        var malformed = Sample(KeyA) with { Key = null! };

        var act = () => SparkplugPayloadEncoder.EncodeNData(
            Seq0, Publication, [malformed], AliasMap, isHistorical: false);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.IdentityInvalid);
    }

    // ==== Equal key + equal wire timestamp: encounter order (slice-4 review r1) ====

    [Fact]
    public void EncodeNData_EqualKeyAndEqualWireTimestamp_PreservesCallerEncounterOrder()
    {
        // Two observations of the SAME metric collapsing to the SAME wire
        // millisecond after truncation; values 10 then 20 distinguish them.
        var baseTime = DateTimeOffset.UnixEpoch.AddSeconds(50);
        var first = Sample(KeyA, value: 10) with { AcquisitionTimestamp = baseTime.AddTicks(1_000) };
        var second = Sample(KeyA, value: 20) with { AcquisitionTimestamp = baseTime.AddTicks(7_000) };

        var bytes = SparkplugPayloadEncoder.EncodeNData(
            Seq0, Publication, [first, second], AliasMap, isHistorical: false);

        var values = Metrics(bytes).Select(m => Field(m, 10)!.VarintValue).ToList();
        values.Should().Equal([10UL, 20UL],
            "for equal key and equal wire timestamp the stable sort preserves caller encounter order (frozen profile policy)");
    }

    // ==== Alias-table and payload rejections ====

    [Fact]
    public void EncodeNBirth_AliasZeroInMap_ThrowsAliasZeroReserved()
    {
        var map = new Dictionary<SparkplugAliasKey, ulong> { [KeyA] = 0 };

        var act = () => SparkplugPayloadEncoder.EncodeNBirth(Seq0, BdSeq7, BdSeqAlias, Publication, [Sample(KeyA)], map);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.AliasZeroReserved);
    }

    [Fact]
    public void EncodeNBirth_BdSeqAliasZero_ThrowsAliasZeroReserved()
    {
        var act = () => SparkplugPayloadEncoder.EncodeNBirth(Seq0, BdSeq7, 0, Publication, [], AliasMap);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.AliasZeroReserved);
    }

    [Fact]
    public void EncodeNBirth_DuplicateAliasValues_ThrowsAliasDuplicate()
    {
        var map = new Dictionary<SparkplugAliasKey, ulong> { [KeyA] = 2, [KeyB] = 2 };

        var act = () => SparkplugPayloadEncoder.EncodeNBirth(Seq0, BdSeq7, BdSeqAlias, Publication, [], map);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.AliasDuplicate);
    }

    [Fact]
    public void EncodeNBirth_AppAliasCollidingWithBdSeqAlias_ThrowsAliasDuplicate()
    {
        var map = new Dictionary<SparkplugAliasKey, ulong> { [KeyA] = BdSeqAlias };

        var act = () => SparkplugPayloadEncoder.EncodeNBirth(Seq0, BdSeq7, BdSeqAlias, Publication, [], map);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.AliasDuplicate);
    }

    [Fact]
    public void EncodeNData_SampleWithoutAlias_ThrowsAliasMissing()
    {
        var unknown = SparkplugAliasKey.Create("cnc", "m1", "gamma");

        var act = () => SparkplugPayloadEncoder.EncodeNData(Seq0, Publication, [Sample(unknown)], AliasMap, isHistorical: false);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.AliasMissing);
    }

    [Fact]
    public void EncodeNBirth_DuplicateBirthMetric_ThrowsPayloadDuplicateBirthMetric()
    {
        var act = () => SparkplugPayloadEncoder.EncodeNBirth(
            Seq0, BdSeq7, BdSeqAlias, Publication, [Sample(KeyA), Sample(KeyA)], AliasMap);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.PayloadDuplicateBirthMetric);
    }

    [Fact]
    public void EncodeNData_EmptySamples_ThrowsPayloadEmpty()
    {
        var act = () => SparkplugPayloadEncoder.EncodeNData(Seq0, Publication, [], AliasMap, isHistorical: false);

        act.Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.PayloadEmpty);
    }
}
