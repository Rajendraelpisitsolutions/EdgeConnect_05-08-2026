// ============================================================================
// File: Birth/SparkplugBirthLayerTests.cs
// Purpose: Locks the K3 slice-3 pure birth-plan / mapping layer (review r1): birth
//          planning + immutable plan/baseline; the wire-EXACT comparator built on the
//          shared K2 mapper (bit-exact float/double incl. +0/-0 and NaN payloads,
//          byte-content, ms timestamp, null-invariant + type rejections, wire-parity);
//          alias resolution (exact set, alias-0, duplicate); the fail-closed cutover
//          comparison (10->20->10, missing/first-observed surfaced); the material
//          classifier + ThrowIfMaterialMutation; and the reserved/duplicate name validators.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Core.Routing;
using ElpisEdgeConnect.Sinks.SparkplugB;
using ElpisEdgeConnect.Sinks.SparkplugB.Birth;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;
using ElpisEdgeConnect.Sinks.SparkplugB.Mapping;
using ElpisEdgeConnect.Sinks.SparkplugB.Payloads;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests.Birth;

public sealed class SparkplugBirthLayerTests
{
    private static readonly DateTimeOffset Ts = new(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Utc = new(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // ==== SparkplugBirthPlanner.Plan ====

    [Fact]
    public void Plan_EmptySnapshot_IsEmptyPlan()
    {
        var plan = SparkplugBirthPlanner.Plan(LatestValueSnapshot.CreateEmpty(RouteSchemaGeneration.Create(1)));

        plan.IsEmpty.Should().BeTrue();
        plan.Metrics.Should().BeEmpty();
        plan.ManifestKeys.Should().BeEmpty();
        plan.Baseline.Should().BeEmpty();
        plan.Schema.Should().BeEmpty();
    }

    [Fact]
    public void Plan_PopulatedSnapshot_OrdinalOrder_ObservedSet()
    {
        var plan = SparkplugBirthPlanner.Plan(Snapshot(
            Lmv("srcC", CanonicalValueType.Integer, 3),
            Lmv("srcA", CanonicalValueType.Double, 1.0),
            Lmv("srcB", CanonicalValueType.Boolean, true)));

        plan.Metrics.Select(m => m.Key.MetricName).Should().Equal("srcA/dev/temp", "srcB/dev/temp", "srcC/dev/temp");
        plan.Baseline.Should().HaveCount(3);
        plan.Schema[AliasKey("srcA")].DataType.Should().Be(SparkplugDataType.Double);
    }

    [Fact]
    public void Plan_InvalidSnapshotValue_FailsClosed_BeforeAliasResolution()
    {
        // Core validates CLR type at snapshot construction, but not the Sparkplug epoch rule:
        // a pre-epoch acquisition timestamp reaches the shared mapper and is rejected during
        // planning — before any alias resolution / bdSeq reservation / CONNECT.
        var lmv = LatestMetricValue.Create(
            CanonicalMetricKey.Create("srcA", "dev", "temp"), CanonicalValueType.Integer, 1,
            isNull: false, new DateTimeOffset(1969, 1, 1, 0, 0, 0, TimeSpan.Zero), DataQuality.Good,
            routeBufferSequence: 1);

        ((Action)(() => SparkplugBirthPlanner.Plan(Snapshot(lmv)))).Should().Throw<AdapterException>()
            .Which.Error.Code.Should().Be(SparkplugErrors.EncodeTimestampPreEpoch);
    }

    [Fact]
    public void Plan_CollectionsAreImmutable()
    {
        // The public surface is ImmutableArray / FrozenDictionary — a caller cannot even
        // compile a cast to a mutable Dictionary/List. Confirm the frozen concrete types.
        var plan = SparkplugBirthPlanner.Plan(Snapshot(Lmv("srcA", CanonicalValueType.Integer, 1)));
        plan.Baseline.Should().BeAssignableTo<System.Collections.Frozen.FrozenDictionary<SparkplugAliasKey, SparkplugMetricState>>();
        plan.Schema.Should().BeAssignableTo<System.Collections.Frozen.FrozenDictionary<SparkplugAliasKey, SparkplugMetricSchema>>();
    }

    // ==== SparkplugMetricState — wire-EXACT comparator on the shared mapper ====

    [Fact]
    public void MetricState_SameWireState_AreEqual() =>
        State(CanonicalValueType.Integer, 10).Should().Be(State(CanonicalValueType.Integer, 10));

    [Fact]
    public void MetricState_DifferentValue_NotEqual() =>
        State(CanonicalValueType.Integer, 10).Should().NotBe(State(CanonicalValueType.Integer, 20));

    [Fact]
    public void MetricState_StringValue_IsCaseSensitive() =>
        State(CanonicalValueType.String, "abc").Should().NotBe(State(CanonicalValueType.String, "ABC"));

    [Fact]
    public void MetricState_ByteArrays_CompareByContent()
    {
        State(CanonicalValueType.ByteArray, new byte[] { 1, 2, 3 })
            .Should().Be(State(CanonicalValueType.ByteArray, new byte[] { 1, 2, 3 }));
        State(CanonicalValueType.ByteArray, new byte[] { 1, 2, 3 })
            .Should().NotBe(State(CanonicalValueType.ByteArray, new byte[] { 1, 2, 4 }));
    }

    [Fact]
    public void MetricState_PositiveAndNegativeZero_AreDistinct_MatchingWire()
    {
        State(CanonicalValueType.Double, 0.0).Should().NotBe(State(CanonicalValueType.Double, -0.0));
        State(CanonicalValueType.Float, 0.0f).Should().NotBe(State(CanonicalValueType.Float, -0.0f));

        // Parity: the encoder distinguishes them too.
        EncodeOne(Sample(CanonicalValueType.Double, 0.0))
            .Should().NotEqual(EncodeOne(Sample(CanonicalValueType.Double, -0.0)));
    }

    [Fact]
    public void MetricState_NaNPayloads_DistinctBits_AreUnequal_SameBits_Equal()
    {
        var nanA = BitConverter.UInt64BitsToDouble(0x7FF8_0000_0000_0000UL);
        var nanB = BitConverter.UInt64BitsToDouble(0x7FF8_0000_0000_0001UL);

        State(CanonicalValueType.Double, nanA).Should().NotBe(State(CanonicalValueType.Double, nanB));
        State(CanonicalValueType.Double, nanA).Should().Be(State(CanonicalValueType.Double, nanA));
    }

    [Fact]
    public void MetricState_ByteContentParity_EqualStatesEncodeIdentically()
    {
        State(CanonicalValueType.ByteArray, new byte[] { 9, 8, 7 })
            .Should().Be(State(CanonicalValueType.ByteArray, new byte[] { 9, 8, 7 }));
        EncodeOne(Sample(CanonicalValueType.ByteArray, new byte[] { 9, 8, 7 }))
            .Should().Equal(EncodeOne(Sample(CanonicalValueType.ByteArray, new byte[] { 9, 8, 7 })));
    }

    [Fact]
    public void MetricState_DateTimeValues_CompareByEncodedMillis()
    {
        var dt = new DateTime(2022, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        State(CanonicalValueType.DateTime, dt).Should().Be(State(CanonicalValueType.DateTime, dt));
        State(CanonicalValueType.DateTime, dt).Should().NotBe(State(CanonicalValueType.DateTime, dt.AddSeconds(1)));
    }

    [Fact]
    public void MetricState_TimestampAtMillisecondPrecision()
    {
        var a = SparkplugMetricState.From(CanonicalValueType.Integer, 1, false, Ts, DataQuality.Good);
        var b = SparkplugMetricState.From(CanonicalValueType.Integer, 1, false, Ts.AddTicks(5000), DataQuality.Good);
        var later = SparkplugMetricState.From(CanonicalValueType.Integer, 1, false, Ts.AddMilliseconds(1), DataQuality.Good);
        a.Should().Be(b);
        a.Should().NotBe(later);
    }

    [Fact]
    public void MetricState_QualityDifference_NotEqual() =>
        SparkplugMetricState.From(CanonicalValueType.Integer, 1, false, Ts, DataQuality.Good)
            .Should().NotBe(SparkplugMetricState.From(CanonicalValueType.Integer, 1, false, Ts, DataQuality.Bad));

    [Fact]
    public void MetricState_NullWithValue_FailsNullInvariant()
    {
        ((Action)(() => SparkplugMetricState.From(CanonicalValueType.Integer, 123, isNull: true, Ts, DataQuality.Good)))
            .Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.EncodeNullInvariant);
    }

    [Theory]
    [InlineData(CanonicalValueType.Integer, "not-an-int")]
    [InlineData(CanonicalValueType.Long, 1)]        // int where long expected
    [InlineData(CanonicalValueType.ByteArray, 12345)]
    public void MetricState_WrongClrType_FailsTypeMismatch(CanonicalValueType type, object value)
    {
        ((Action)(() => SparkplugMetricState.From(type, value, false, Ts, DataQuality.Good)))
            .Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.EncodeValueTypeMismatch);
    }

    // ==== SparkplugBirthPlanner.Resolve (alias resolution) ====

    [Fact]
    public void Resolve_ExactMatch_ProducesAliasMap()
    {
        var plan = SparkplugBirthPlanner.Plan(Snapshot(Lmv("srcA", CanonicalValueType.Integer, 1), Lmv("srcB", CanonicalValueType.Integer, 2)));
        var resolved = SparkplugBirthPlanner.Resolve(plan, new Dictionary<SparkplugAliasKey, ulong>
        {
            [AliasKey("srcA")] = 1, [AliasKey("srcB")] = 2,
        });

        resolved.AliasMap.Should().HaveCount(2);
        resolved.Metrics.Should().HaveCount(2);
        resolved.AliasMap.Should().BeAssignableTo<System.Collections.Frozen.FrozenDictionary<SparkplugAliasKey, ulong>>();
    }

    [Fact]
    public void Resolve_MissingOrExtraAlias_FailsSetMismatch()
    {
        var plan = SparkplugBirthPlanner.Plan(Snapshot(Lmv("srcA", CanonicalValueType.Integer, 1)));

        ((Action)(() => SparkplugBirthPlanner.Resolve(plan, new Dictionary<SparkplugAliasKey, ulong>())))
            .Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.AliasTableMismatch);
        ((Action)(() => SparkplugBirthPlanner.Resolve(plan, new Dictionary<SparkplugAliasKey, ulong>
        {
            [AliasKey("srcA")] = 1, [AliasKey("srcExtra")] = 2,
        }))).Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.AliasTableMismatch);
    }

    [Fact]
    public void Resolve_AliasZero_Rejected()
    {
        var plan = SparkplugBirthPlanner.Plan(Snapshot(Lmv("srcA", CanonicalValueType.Integer, 1)));
        ((Action)(() => SparkplugBirthPlanner.Resolve(plan, new Dictionary<SparkplugAliasKey, ulong> { [AliasKey("srcA")] = 0 })))
            .Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.AliasZeroReserved);
    }

    [Fact]
    public void Resolve_DuplicateAlias_Rejected()
    {
        var plan = SparkplugBirthPlanner.Plan(Snapshot(Lmv("srcA", CanonicalValueType.Integer, 1), Lmv("srcB", CanonicalValueType.Integer, 2)));
        ((Action)(() => SparkplugBirthPlanner.Resolve(plan, new Dictionary<SparkplugAliasKey, ulong>
        {
            [AliasKey("srcA")] = 1, [AliasKey("srcB")] = 1,
        }))).Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.AliasDuplicate);
    }

    // ==== SparkplugBirthBaseline (dirtySinceBirth + fail-closed cutover) ====

    [Fact]
    public void Baseline_ChangedThenReturned_StaysDirty_FinalUpdateEmitsFinalValue()
    {
        var key = AliasKey("srcA");
        var baseline = new SparkplugBirthBaseline(Dict(key, State(CanonicalValueType.Integer, 10)));

        baseline.Observe(key, State(CanonicalValueType.Integer, 20));
        baseline.Observe(key, State(CanonicalValueType.Integer, 10));

        baseline.IsDirty(key).Should().BeTrue();
        var cmp = baseline.Compare(Dict(key, State(CanonicalValueType.Integer, 10)));
        cmp.FinalUpdates.Should().ContainKey(key);
        cmp.FinalUpdates[key].Should().Be(State(CanonicalValueType.Integer, 10));
        cmp.IsExactManifest.Should().BeTrue();
    }

    [Fact]
    public void Baseline_Unchanged_NotInFinalUpdate()
    {
        var key = AliasKey("srcA");
        var baseline = new SparkplugBirthBaseline(Dict(key, State(CanonicalValueType.Integer, 10)));
        baseline.Observe(key, State(CanonicalValueType.Integer, 10));

        baseline.IsDirty(key).Should().BeFalse();
        baseline.Compare(Dict(key, State(CanonicalValueType.Integer, 10))).FinalUpdates.Should().BeEmpty();
    }

    [Fact]
    public void Baseline_ChangedOnlyAtCutover_InFinalUpdate()
    {
        var key = AliasKey("srcA");
        var baseline = new SparkplugBirthBaseline(Dict(key, State(CanonicalValueType.Integer, 10)));
        baseline.Compare(Dict(key, State(CanonicalValueType.Integer, 20))).FinalUpdates[key]
            .Should().Be(State(CanonicalValueType.Integer, 20));
    }

    [Fact]
    public void Baseline_DirtyMetricMissingAtCutover_SurfacedAsMissing()
    {
        var key = AliasKey("srcA");
        var baseline = new SparkplugBirthBaseline(Dict(key, State(CanonicalValueType.Integer, 10)));
        baseline.Observe(key, State(CanonicalValueType.Integer, 20));

        var cmp = baseline.Compare(new Dictionary<SparkplugAliasKey, SparkplugMetricState>());

        cmp.MissingAnnouncedKeys.Should().Contain(key);
        cmp.IsExactManifest.Should().BeFalse();
    }

    [Fact]
    public void Baseline_ExtraCutoverMetric_SurfacedAsFirstObserved()
    {
        var known = AliasKey("srcA");
        var unknown = AliasKey("srcNew");
        var baseline = new SparkplugBirthBaseline(Dict(known, State(CanonicalValueType.Integer, 10)));

        var cmp = baseline.Compare(new Dictionary<SparkplugAliasKey, SparkplugMetricState>
        {
            [known] = State(CanonicalValueType.Integer, 10),
            [unknown] = State(CanonicalValueType.Integer, 5),
        });

        cmp.FirstObservedKeys.Should().Contain(unknown);
        cmp.FinalUpdates.Should().NotContainKey(unknown); // not silently included as a final update
    }

    [Fact]
    public void Baseline_DefensivelyCopiesSource()
    {
        var key = AliasKey("srcA");
        var source = new Dictionary<SparkplugAliasKey, SparkplugMetricState> { [key] = State(CanonicalValueType.Integer, 10) };
        var baseline = new SparkplugBirthBaseline(source);

        source[key] = State(CanonicalValueType.Integer, 999); // mutate after construction
        baseline.Observe(key, State(CanonicalValueType.Integer, 10));

        baseline.IsDirty(key).Should().BeFalse(); // still compares against the original 10, not 999
    }

    // ==== Classifier + fail-closed guard ====

    [Fact]
    public void Classify_KnownUnchanged() =>
        SparkplugMaterialSchemaClassifier.Classify(
            Dict(AliasKey("srcA"), SparkplugMetricSchema.From(CanonicalValueType.Integer)),
            AliasKey("srcA"), SparkplugMetricSchema.From(CanonicalValueType.Integer))
            .Should().Be(SparkplugMetricClassification.KnownUnchanged);

    [Fact]
    public void Classify_NewKey_FirstObserved() =>
        SparkplugMaterialSchemaClassifier.Classify(
            Dict(AliasKey("srcA"), SparkplugMetricSchema.From(CanonicalValueType.Integer)),
            AliasKey("srcNew"), SparkplugMetricSchema.From(CanonicalValueType.Integer))
            .Should().Be(SparkplugMetricClassification.FirstObserved);

    [Fact]
    public void Classify_DatatypeChange_MaterialMutation() =>
        SparkplugMaterialSchemaClassifier.Classify(
            Dict(AliasKey("srcA"), SparkplugMetricSchema.From(CanonicalValueType.Integer)),
            AliasKey("srcA"), SparkplugMetricSchema.From(CanonicalValueType.Long))
            .Should().Be(SparkplugMetricClassification.MaterialMutation);

    [Fact]
    public void Classify_FromDataPoint_SameDatatype_KnownUnchanged() =>
        SparkplugMaterialSchemaClassifier.Classify(
            Dict(AliasKey("srcA"), SparkplugMetricSchema.From(CanonicalValueType.Integer)),
            Point("srcA", CanonicalValueType.Integer, 1))
            .Should().Be(SparkplugMetricClassification.KnownUnchanged);

    [Fact]
    public void Classify_FromDataPoint_ChangedDatatype_MaterialMutation() =>
        SparkplugMaterialSchemaClassifier.Classify(
            Dict(AliasKey("srcA"), SparkplugMetricSchema.From(CanonicalValueType.Integer)),
            Point("srcA", CanonicalValueType.Long, 1L))
            .Should().Be(SparkplugMetricClassification.MaterialMutation);

    [Fact]
    public void ThrowIfMaterialMutation_MaterialMutation_FailsClosed()
    {
        ((Action)(() => SparkplugMaterialSchemaClassifier.ThrowIfMaterialMutation(SparkplugMetricClassification.MaterialMutation, "m")))
            .Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.MaterialSchemaMutation);
    }

    [Fact]
    public void ThrowIfMaterialMutation_KnownUnchanged_DoesNotThrow() =>
        FluentActions.Invoking(
            () => SparkplugMaterialSchemaClassifier.ThrowIfMaterialMutation(SparkplugMetricClassification.KnownUnchanged, "m"))
            .Should().NotThrow();

    [Fact]
    public void ThrowIfMaterialMutation_FirstObserved_DoesNotThrow() =>
        FluentActions.Invoking(
            () => SparkplugMaterialSchemaClassifier.ThrowIfMaterialMutation(SparkplugMetricClassification.FirstObserved, "m"))
            .Should().NotThrow();

    // ==== Name validators (directly testable) ====

    [Theory]
    [InlineData("bdSeq")]
    [InlineData("Node Control/Rebirth")]
    public void RequireNotReserved_ReservedName_Throws(string name) =>
        FluentActions.Invoking(() => SparkplugBirthMetricNames.RequireNotReserved(name))
            .Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.BirthReservedMetricName);

    [Fact]
    public void RequireNotReserved_OrdinaryName_Ok() =>
        FluentActions.Invoking(() => SparkplugBirthMetricNames.RequireNotReserved("src/dev/temp")).Should().NotThrow();

    [Fact]
    public void ValidateAll_DuplicateName_Throws() =>
        FluentActions.Invoking(() => SparkplugBirthMetricNames.ValidateAll(new[] { "a/b/c", "a/b/c" }))
            .Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.BirthDuplicateMetricName);

    [Fact]
    public void ValidateAll_CaseOnlyNames_RemainDistinct() =>
        FluentActions.Invoking(() => SparkplugBirthMetricNames.ValidateAll(new[] { "a/b/c", "a/b/C" })).Should().NotThrow();

    [Fact]
    public void ThrowIfMaterialMutation_UndefinedClassification_FailsClosed() =>
        FluentActions.Invoking(() => SparkplugMaterialSchemaClassifier.ThrowIfMaterialMutation((SparkplugMetricClassification)99, "m"))
            .Should().Throw<ArgumentOutOfRangeException>();

    // ==== FromDataPoint — strict UTC acquisition timestamp (deterministic) ====

    [Fact]
    public void FromDataPoint_UtcTimestamp_PreservesInstant()
    {
        var utc = new DateTime(2022, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        SparkplugMetricState.FromDataPoint(PointTs(utc)).TimestampMs
            .Should().Be(SparkplugTimestamp.ToUnixMilliseconds(new DateTimeOffset(utc, TimeSpan.Zero)));
    }

    [Fact]
    public void FromDataPoint_LocalTimestamp_FailsClosed() =>
        ((Action)(() => SparkplugMetricState.FromDataPoint(PointTs(new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Local)))))
            .Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.EncodeTimestampNotUtc);

    [Fact]
    public void FromDataPoint_UnspecifiedTimestamp_FailsClosed() =>
        ((Action)(() => SparkplugMetricState.FromDataPoint(PointTs(new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)))))
            .Should().Throw<AdapterException>().Which.Error.Code.Should().Be(SparkplugErrors.EncodeTimestampNotUtc);

    // ==== Wire parity across the full K2 type table ====

    [Theory]
    [InlineData(CanonicalValueType.Boolean)]
    [InlineData(CanonicalValueType.Integer)]
    [InlineData(CanonicalValueType.Long)]
    [InlineData(CanonicalValueType.Float)]
    [InlineData(CanonicalValueType.Double)]
    [InlineData(CanonicalValueType.String)]
    [InlineData(CanonicalValueType.DateTime)]
    [InlineData(CanonicalValueType.ByteArray)]
    public void MetricState_WireParity_EqualStatesEncodeIdentically(CanonicalValueType type)
    {
        State(type, ParityValue(type)).Should().Be(State(type, ParityValue(type)));
        EncodeOne(Sample(type, ParityValue(type))).Should().Equal(EncodeOne(Sample(type, ParityValue(type))));
    }

    // ==== Helpers ====

    private static object ParityValue(CanonicalValueType type) => type switch
    {
        CanonicalValueType.Boolean => true,
        CanonicalValueType.Integer => 42,
        CanonicalValueType.Long => 42L,
        CanonicalValueType.Float => 1.5f,
        CanonicalValueType.Double => 1.5,
        CanonicalValueType.String => "x",
        CanonicalValueType.DateTime => new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        CanonicalValueType.ByteArray => new byte[] { 1, 2, 3 },
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    private static CanonicalDataPoint PointTs(DateTime deviceTimestamp) => new()
    {
        GatewayId = "gw",
        SourceInstanceId = "srcA",
        ProtocolName = "sparkplug-b",
        DeviceId = "dev",
        TagName = "temp",
        TagPath = "temp",
        Value = 1,
        ValueType = CanonicalValueType.Integer,
        Quality = DataQuality.Good,
        DeviceTimestamp = deviceTimestamp,
        GatewayTimestamp = Utc,
    };

    private static SparkplugAliasKey AliasKey(string source) =>
        SparkplugAliasKey.FromCanonical(CanonicalMetricKey.Create(source, "dev", "temp"));

    private static SparkplugMetricState State(CanonicalValueType type, object? value) =>
        SparkplugMetricState.From(type, value, isNull: false, Ts, DataQuality.Good);

    private static SparkplugMetricSample Sample(CanonicalValueType type, object? value) => new()
    {
        Key = AliasKey("srcA"),
        ValueType = type,
        Value = value,
        IsNull = false,
        AcquisitionTimestamp = Ts,
        Quality = DataQuality.Good,
    };

    private static byte[] EncodeOne(SparkplugMetricSample sample) =>
        SparkplugPayloadEncoder.EncodeNData(
            SparkplugSequenceNumber.Create(1), Ts, new[] { sample },
            new Dictionary<SparkplugAliasKey, ulong> { [sample.Key] = 1UL }, isHistorical: false);

    private static LatestMetricValue Lmv(string source, CanonicalValueType type, object? value) =>
        LatestMetricValue.Create(
            CanonicalMetricKey.Create(source, "dev", "temp"), type, value, isNull: false, Ts, DataQuality.Good,
            routeBufferSequence: 1);

    private static LatestValueSnapshot Snapshot(params LatestMetricValue[] values) =>
        new(RouteSchemaGeneration.Create(1), values.ToDictionary(v => v.Metric));

    private static CanonicalDataPoint Point(string source, CanonicalValueType type, object? value) => new()
    {
        GatewayId = "gw",
        SourceInstanceId = source,
        ProtocolName = "sparkplug-b",
        DeviceId = "dev",
        TagName = "temp",
        TagPath = "temp",
        Value = value,
        ValueType = type,
        Quality = DataQuality.Good,
        DeviceTimestamp = Utc,
        GatewayTimestamp = Utc,
    };

    private static object DefaultValue(CanonicalValueType type) => type == CanonicalValueType.Long ? 1L : 1;

    private static Dictionary<SparkplugAliasKey, T> Dict<T>(SparkplugAliasKey key, T value) => new() { [key] = value };
}
