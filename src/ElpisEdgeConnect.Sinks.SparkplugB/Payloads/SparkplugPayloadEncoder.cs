// ============================================================================
// File: Payloads/SparkplugPayloadEncoder.cs
// Purpose: THE public encode surface of the Sparkplug B wire layer (ADR-0035
//          Rule 2): NBIRTH / NDATA / NDEATH factories over the validated-model
//          pipeline. Implements the frozen rules:
//          - Alias policy (plan v2 §3.1): every ordinary metric name+alias in
//            NBIRTH and alias-only in NDATA; aliases unique, non-zero; bdSeq
//            HAS an alias (explicit parameter — it has no SparkplugAliasKey,
//            which is what structurally excludes well-known metrics from the
//            map); 'Node Control/Rebirth' is name-only, NO alias (spec
//            [tck-id-operational-behavior-data-commands-rebirth-name-aliases]).
//          - Deterministic ordering (plan v2 §3.7): NBIRTH = bdSeq first,
//            Node Control/Rebirth second, application metrics by ordinal
//            alias-key metric name; NDATA = chronological by metric timestamp
//            (spec MUST for non-historical values,
//            [tck-id-operational-behavior-data-publish-nbirth-order]; applied
//            to historical too as profile policy, plan v3 §3.1), alias-key
//            ordinal as the deterministic tiebreak.
//          - NDEATH frozen field-presence profile (plan v2 §3.6): no payload
//            timestamp, no seq, exactly one metric 'bdSeq' (Int64, value, no
//            alias, no metric timestamp). The same bytes serve as the MQTT
//            Will payload and the intentional-disconnect publish.
//          - Payload timestamp = publication instant; metric timestamp =
//            acquisition instant (ADR-0035 Rule 5).
//          All rejections are typed SPARKPLUG.* errors raised before any bytes
//          exist; the projection stage never rejects.
// Reference: ADR-0035 Rules 2/3/5 (as amended); plan v3 (frozen).
// ============================================================================

using ElpisEdgeConnect.Core.Errors;
using ElpisEdgeConnect.Sinks.SparkplugB.Identity;
using ElpisEdgeConnect.Sinks.SparkplugB.Mapping;
using Org.Eclipse.Tahu.Protobuf;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Payloads;

/// <summary>
/// Builds the wire bytes for NBIRTH, NDATA, and NDEATH payloads under the
/// frozen K2 profile. Stateless and pure: <c>seq</c>/<c>bdSeq</c> values,
/// alias assignments, and the publication instant are inputs owned by the K3
/// session actor.
/// </summary>
public static class SparkplugPayloadEncoder
{
    /// <summary>The well-known bdSeq metric name.</summary>
    public const string BdSeqMetricName = "bdSeq";

    /// <summary>The well-known Node Control/Rebirth metric name (announced false in every NBIRTH).</summary>
    public const string NodeControlRebirthMetricName = "Node Control/Rebirth";

    /// <summary>Encode an NBIRTH payload.</summary>
    /// <param name="seq">The message sequence number (the K3 actor starts sessions at 0; any 0..255 value is spec-legal).</param>
    /// <param name="bdSeq">The birth/death sequence (must equal the value in the paired NDEATH Will).</param>
    /// <param name="bdSeqAlias">The alias reserved for the bdSeq metric (non-zero, unique vs. the alias map).</param>
    /// <param name="publicationTimestamp">The publication instant (payload timestamp; also stamps the well-known metrics).</param>
    /// <param name="metrics">The birth snapshot: one current value per metric (empty for an empty route).</param>
    /// <param name="aliasMap">
    /// The Edge-Node alias table for application metrics. Its keys must be an
    /// EXACT set match with <paramref name="metrics"/> — every alias usable by
    /// a later NDATA must be announced in this NBIRTH (an empty route requires
    /// an empty application alias map).
    /// </param>
    /// <returns>The NBIRTH wire bytes.</returns>
    /// <exception cref="AdapterException">Thrown with a <see cref="SparkplugErrors"/> code for every rejection path.</exception>
    public static byte[] EncodeNBirth(
        SparkplugSequenceNumber seq,
        SparkplugBirthDeathSequence bdSeq,
        ulong bdSeqAlias,
        DateTimeOffset publicationTimestamp,
        IReadOnlyList<SparkplugMetricSample> metrics,
        IReadOnlyDictionary<SparkplugAliasKey, ulong> aliasMap)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(aliasMap);

        var publicationMs = SparkplugTimestamp.ToUnixMilliseconds(publicationTimestamp);
        ValidateSamples(metrics);
        RequireDistinctBirthMetrics(metrics);
        ValidateAliasTable(aliasMap, bdSeqAlias);
        RequireExactAliasCoverage(metrics, aliasMap);

        var payload = new Payload
        {
            Timestamp = publicationMs,
            Seq = seq.WireValue,
        };

        // Frozen ordering: bdSeq first, Node Control/Rebirth second, then
        // application metrics by ordinal metric name.
        payload.Metrics.Add(SparkplugPayloadProjection.ProjectMetric(
            WellKnownBdSeq(bdSeq, publicationMs), BdSeqMetricName, bdSeqAlias, isHistorical: false));
        payload.Metrics.Add(SparkplugPayloadProjection.ProjectMetric(
            WellKnownRebirthFalse(publicationMs), NodeControlRebirthMetricName, alias: null, isHistorical: false));

        foreach (var sample in metrics.OrderBy(m => m.Key.MetricName, StringComparer.Ordinal))
        {
            var model = MapSample(sample);
            var alias = ResolveAlias(aliasMap, sample.Key);
            payload.Metrics.Add(SparkplugPayloadProjection.ProjectMetric(
                model, sample.Key.MetricName, alias, isHistorical: false));
        }

        return SparkplugPayloadProjection.Serialize(payload);
    }

    /// <summary>Encode an NDATA payload (alias-only metrics, no names).</summary>
    /// <param name="seq">The message sequence number for this message.</param>
    /// <param name="publicationTimestamp">The publication instant (payload timestamp).</param>
    /// <param name="samples">The change events (at least one; multiple samples per metric allowed).</param>
    /// <param name="aliasMap">The Edge-Node alias table (every sample must resolve).</param>
    /// <param name="isHistorical">Whether this batch is replayed history (<c>is_historical=true</c> on every metric).</param>
    /// <returns>The NDATA wire bytes.</returns>
    /// <exception cref="AdapterException">Thrown with a <see cref="SparkplugErrors"/> code for every rejection path.</exception>
    public static byte[] EncodeNData(
        SparkplugSequenceNumber seq,
        DateTimeOffset publicationTimestamp,
        IReadOnlyList<SparkplugMetricSample> samples,
        IReadOnlyDictionary<SparkplugAliasKey, ulong> aliasMap,
        bool isHistorical)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(aliasMap);

        if (samples.Count == 0)
        {
            throw Reject(SparkplugErrors.PayloadEmpty, "An NDATA payload requires at least one sample.");
        }

        var publicationMs = SparkplugTimestamp.ToUnixMilliseconds(publicationTimestamp);
        ValidateSamples(samples);
        ValidateAliasTable(aliasMap, bdSeqAlias: null);

        var payload = new Payload
        {
            Timestamp = publicationMs,
            Seq = seq.WireValue,
        };

        // Chronological order (spec MUST for non-historical; profile policy for
        // historical), alias-key ordinal as the deterministic tiebreak. Mapping
        // happens before ordering so the sort key is the validated wire
        // timestamp, not the raw input. For samples with EQUAL key and EQUAL
        // wire timestamp (possible after millisecond truncation), the stable
        // sort preserves CALLER ENCOUNTER ORDER — that is the frozen profile
        // policy and the final ordering dimension (slice-4 review r1).
        var mapped = samples
            .Select(sample => (Model: MapSample(sample), Alias: ResolveAlias(aliasMap, sample.Key), sample.Key))
            .OrderBy(m => m.Model.TimestampMs)
            .ThenBy(m => m.Key.MetricName, StringComparer.Ordinal)
            .ToList();

        foreach (var (model, alias, _) in mapped)
        {
            payload.Metrics.Add(SparkplugPayloadProjection.ProjectMetric(
                model, name: null, alias, isHistorical));
        }

        return SparkplugPayloadProjection.Serialize(payload);
    }

    /// <summary>
    /// Encode the NDEATH payload (the MQTT Will payload; the intentional-
    /// disconnect publish reuses these same bytes). Frozen profile: no payload
    /// timestamp, no seq, exactly one metric — <c>bdSeq</c>, Int64, no alias,
    /// no metric timestamp, value equal to the paired NBIRTH's bdSeq.
    /// </summary>
    /// <param name="bdSeq">The birth/death sequence.</param>
    /// <returns>The NDEATH wire bytes.</returns>
    public static byte[] EncodeNDeath(SparkplugBirthDeathSequence bdSeq)
    {
        var payload = new Payload();
        payload.Metrics.Add(SparkplugPayloadProjection.ProjectMetric(
            WellKnownBdSeq(bdSeq, timestampMs: 0), BdSeqMetricName, alias: null, isHistorical: false, includeTimestamp: false));

        return SparkplugPayloadProjection.Serialize(payload);
    }

    private static SparkplugMetricValueModel MapSample(SparkplugMetricSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return SparkplugMetricValueMapper.Map(
            sample.ValueType, sample.Value, sample.IsNull, sample.AcquisitionTimestamp, sample.Quality);
    }

    private static SparkplugMetricValueModel WellKnownBdSeq(SparkplugBirthDeathSequence bdSeq, ulong timestampMs) => new()
    {
        DataType = SparkplugDataType.Int64,
        IsNull = false,
        TimestampMs = timestampMs,
        UInt64Bits = bdSeq.WireValue,
    };

    private static SparkplugMetricValueModel WellKnownRebirthFalse(ulong timestampMs) => new()
    {
        DataType = SparkplugDataType.Boolean,
        IsNull = false,
        TimestampMs = timestampMs,
        BooleanValue = false,
    };

    private static void ValidateAliasTable(IReadOnlyDictionary<SparkplugAliasKey, ulong> aliasMap, ulong? bdSeqAlias)
    {
        var seen = new Dictionary<ulong, string>();
        if (bdSeqAlias is { } reserved)
        {
            if (reserved == 0)
            {
                throw Reject(SparkplugErrors.AliasZeroReserved, "Alias 0 is reserved; the bdSeq alias must be >= 1.");
            }

            seen.Add(reserved, BdSeqMetricName);
        }

        foreach (var (key, alias) in aliasMap)
        {
            if (alias == 0)
            {
                throw Reject(SparkplugErrors.AliasZeroReserved,
                    $"Alias 0 is reserved; metric '{key.MetricName}' must use an alias >= 1.");
            }

            if (!seen.TryAdd(alias, key.MetricName))
            {
                throw Reject(SparkplugErrors.AliasDuplicate,
                    $"Alias {alias} is assigned to both '{seen[alias]}' and '{key.MetricName}'; aliases must be unique across the Edge Node.");
            }
        }
    }

    private static ulong ResolveAlias(IReadOnlyDictionary<SparkplugAliasKey, ulong> aliasMap, SparkplugAliasKey key)
    {
        if (!aliasMap.TryGetValue(key, out var alias))
        {
            throw Reject(SparkplugErrors.AliasMissing,
                $"Metric '{key.MetricName}' has no alias in the supplied alias map; every published metric requires an established alias.");
        }

        return alias;
    }

    /// <summary>
    /// Structural sample validation, run BEFORE any ordering, lookup, or set
    /// comparison: a null list element is a caller API violation
    /// (<see cref="ArgumentNullException"/>, project convention), while a
    /// non-null sample with a null alias key is a typed rejection — a
    /// malformed metric key must never surface as an incidental
    /// NullReferenceException (slice-4 review r1).
    /// </summary>
    private static void ValidateSamples(IReadOnlyList<SparkplugMetricSample> samples)
    {
        foreach (var sample in samples)
        {
            ArgumentNullException.ThrowIfNull(sample, nameof(samples));
            if (sample.Key is null)
            {
                throw Reject(SparkplugErrors.IdentityInvalid,
                    "A Sparkplug metric sample requires a non-null alias key.");
            }
        }
    }

    /// <summary>
    /// The NBIRTH alias-baseline invariant (slice-4 review r1): application
    /// metric keys and application alias-map keys must be an EXACT set match.
    /// Every birth metric needs an alias, and every alias later usable by
    /// NDATA must be physically announced (name + datatype) in this NBIRTH —
    /// a receiver can only resolve aliases established in the active birth.
    /// Surplus aliases are never silently ignored.
    /// </summary>
    private static void RequireExactAliasCoverage(
        IReadOnlyList<SparkplugMetricSample> metrics,
        IReadOnlyDictionary<SparkplugAliasKey, ulong> aliasMap)
    {
        var metricKeys = metrics.Select(m => m.Key).ToHashSet();
        var missingAliases = metricKeys.Where(k => !aliasMap.ContainsKey(k)).Select(k => k.MetricName).Order(StringComparer.Ordinal).ToList();
        var unannounced = aliasMap.Keys.Where(k => !metricKeys.Contains(k)).Select(k => k.MetricName).Order(StringComparer.Ordinal).ToList();

        if (missingAliases.Count > 0 || unannounced.Count > 0)
        {
            throw Reject(SparkplugErrors.AliasTableMismatch,
                "NBIRTH application metrics and the application alias map must be an exact set match. " +
                $"Metrics missing aliases: [{string.Join(", ", missingAliases)}]; " +
                $"aliases with no birth metric: [{string.Join(", ", unannounced)}].");
        }
    }

    private static void RequireDistinctBirthMetrics(IReadOnlyList<SparkplugMetricSample> metrics)
    {
        var seen = new HashSet<SparkplugAliasKey>();
        foreach (var sample in metrics)
        {
            if (!seen.Add(sample.Key))
            {
                throw Reject(SparkplugErrors.PayloadDuplicateBirthMetric,
                    $"NBIRTH announces metric '{sample.Key.MetricName}' more than once; birth carries one current value per metric.");
            }
        }
    }

    private static AdapterException Reject(string code, string message) =>
        new(new AdapterError
        {
            Code = code,
            Category = ErrorCategory.Internal,
            Message = message,
            Retryable = false,
        });
}
