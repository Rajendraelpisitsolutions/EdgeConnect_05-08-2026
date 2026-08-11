// ============================================================================
// File: SparkplugErrors.cs
// Purpose: Stable error code catalog for the Sparkplug B sink module. Every
//          error code thrown from this module is defined here (CLAUDE.md §9
//          rule 12). Codes follow the locked MODULE.CATEGORY_SUBCATEGORY
//          convention. Mapper/encoder rejections are raised at the
//          validated-model stage (plan v2 §3.8) — the final protobuf
//          serialization is effectively infallible.
// Reference: ADR-0035 Rule 5 (value mapping; typed rejection, never silent
//            drop); plan v3 (frozen) §3.3; ARCHITECTURE_BLUEPRINT.md §13.3.
// ============================================================================

namespace ElpisEdgeConnect.Sinks.SparkplugB;

/// <summary>
/// Stable catalog of Sparkplug B sink error codes. String constants rather
/// than an enum, matching <c>CoreErrors</c>. Adding a code is allowed;
/// removing or renaming an existing code is a breaking change.
/// </summary>
public static class SparkplugErrors
{
    // ==== Encoding / value mapping (validated-model stage) ====

    /// <summary>
    /// The canonical value type has no scalar Sparkplug equivalent
    /// (<c>Array</c>, <c>Object</c>), declares no real type (<c>Null</c>), or
    /// is undefined. Never silently dropped (ADR-0035 Rule 5).
    /// </summary>
    public const string EncodeUnmappableDatatype = "SPARKPLUG.ENCODE_UNMAPPABLE_DATATYPE";

    /// <summary>The runtime CLR type of a value does not match its declared canonical value type.</summary>
    public const string EncodeValueTypeMismatch = "SPARKPLUG.ENCODE_VALUE_TYPE_MISMATCH";

    /// <summary>The null invariant is violated: IsNull=true with a value, or IsNull=false without one.</summary>
    public const string EncodeNullInvariant = "SPARKPLUG.ENCODE_NULL_INVARIANT";

    /// <summary>
    /// A timestamp precedes the Unix epoch (1970-01-01T00:00:00Z). Sparkplug
    /// timestamps are unsigned 64-bit milliseconds; a pre-epoch instant is
    /// rejected rather than cast into a fabricated future time (plan v2 §3.3).
    /// </summary>
    public const string EncodeTimestampPreEpoch = "SPARKPLUG.ENCODE_TIMESTAMP_PRE_EPOCH";

    /// <summary>A timestamp exceeds the representable unsigned 64-bit millisecond range (rejected, never wrapped).</summary>
    public const string EncodeTimestampOverflow = "SPARKPLUG.ENCODE_TIMESTAMP_OVERFLOW";

    /// <summary>
    /// A canonical acquisition <see cref="System.DateTime"/> is not <see cref="System.DateTimeKind.Utc"/>.
    /// Fail loud rather than silently reinterpret a Local/Unspecified instant with the machine's
    /// timezone (which would make encoding non-deterministic across gateways) — matching Core's
    /// tracked-route policy (slice-3 review r2).
    /// </summary>
    public const string EncodeTimestampNotUtc = "SPARKPLUG.ENCODE_TIMESTAMP_NOT_UTC";

    /// <summary>The canonical data quality is not a defined <c>DataQuality</c> value.</summary>
    public const string EncodeQualityUndefined = "SPARKPLUG.ENCODE_QUALITY_UNDEFINED";

    // ==== Identity / namespace ====

    /// <summary>A Sparkplug identity element (group id, edge node id, alias-key component) is empty or contains a forbidden character.</summary>
    public const string IdentityInvalid = "SPARKPLUG.IDENTITY_INVALID";

    // ==== Alias table (payload-factory validation, plan v2 §3.1) ====

    /// <summary>Alias 0 is reserved by this profile; assigned aliases begin at 1.</summary>
    public const string AliasZeroReserved = "SPARKPLUG.ALIAS_ZERO_RESERVED";

    /// <summary>Two metrics share one alias value — aliases must be unique across the Edge Node.</summary>
    public const string AliasDuplicate = "SPARKPLUG.ALIAS_DUPLICATE";

    /// <summary>A metric has no alias in the supplied map (every ordinary NBIRTH/NDATA metric requires one).</summary>
    public const string AliasMissing = "SPARKPLUG.ALIAS_MISSING";

    /// <summary>
    /// The NBIRTH application metrics and the application alias map are not an
    /// exact set match. Both directions are violations: a birth metric without
    /// an alias, and an alias never announced by a birth metric (a receiver
    /// can only resolve an NDATA alias established in the active NBIRTH).
    /// Surplus aliases are never silently ignored.
    /// </summary>
    public const string AliasTableMismatch = "SPARKPLUG.ALIAS_TABLE_MISMATCH";

    // ==== Payload construction ====

    /// <summary>An NBIRTH announced the same metric more than once (birth carries one current value per metric).</summary>
    public const string PayloadDuplicateBirthMetric = "SPARKPLUG.PAYLOAD_DUPLICATE_BIRTH_METRIC";

    /// <summary>An NDATA payload was requested with no samples.</summary>
    public const string PayloadEmpty = "SPARKPLUG.PAYLOAD_EMPTY";

    // ==== Session establishment (K3 slice 4) ====

    /// <summary>
    /// The actor was asked to begin a session but was not wired with an identity store /
    /// transport (K4 composition), or was not in a startable state. Fail closed.
    /// </summary>
    public const string SessionNotReady = "SPARKPLUG.SESSION_NOT_READY";

    /// <summary>
    /// An NBIRTH publish did not complete at the local transport boundary. Fatal for the
    /// initial Begin (plan v3 §4.5): promotes nothing, faults the route.
    /// </summary>
    public const string BirthPublishFailed = "SPARKPLUG.BIRTH_PUBLISH_FAILED";

    /// <summary>A Begin was requested while a session is already active (single-session actor).</summary>
    public const string SessionAlreadyActive = "SPARKPLUG.SESSION_ALREADY_ACTIVE";

    /// <summary>The connection-generation counter would overflow (fail closed before creating a client).</summary>
    public const string GenerationOverflow = "SPARKPLUG.GENERATION_OVERFLOW";

    /// <summary>The transport dropped (or CONNECT failed) during initial Begin, before an authoritative birth.</summary>
    public const string SessionSuspectDuringBegin = "SPARKPLUG.SESSION_SUSPECT_DURING_BEGIN";

    /// <summary>
    /// A sanitized FALLBACK code for an untyped actor failure — an illegal lifecycle transition or an
    /// unexpected actor-loop exception that carried no structured <c>AdapterError</c>. Ensures a Faulted
    /// actor always exposes a last-error code + time (plan v3 §8; slice-7 review B4). Carries no message,
    /// exception type, or customer data.
    /// </summary>
    public const string ActorFailure = "SPARKPLUG.ACTOR_FAILURE";

    // ==== Transport (K3 slice 4) ====

    /// <summary>CONNECT did not return a success CONNACK.</summary>
    public const string TransportConnectFailed = "SPARKPLUG.TRANSPORT_CONNECT_FAILED";

    /// <summary>The exact NCMD SUBSCRIBE was rejected or granted a QoS below the requested QoS 1.</summary>
    public const string TransportSubscribeFailed = "SPARKPLUG.TRANSPORT_SUBSCRIBE_FAILED";

    // ==== Configuration validation (K3 slice 1) ====

    /// <summary>The supplied configuration is not a <c>SparkplugSinkConfiguration</c>.</summary>
    public const string ConfigWrongType = "SPARKPLUG.CONFIG_WRONG_TYPE";

    /// <summary>
    /// A connection JSON field is present but has the wrong type or an
    /// out-of-range/non-integral numeric value (e.g. <c>"brokerPort": "8883"</c>).
    /// Never silently coerced to the default (slice-1 review B3).
    /// </summary>
    public const string ConfigInvalidFieldType = "SPARKPLUG.CONFIG_INVALID_FIELD_TYPE";

    /// <summary>The required broker host is missing or blank.</summary>
    public const string ConfigMissingBrokerHost = "SPARKPLUG.CONFIG_MISSING_BROKER_HOST";

    /// <summary>The broker port is outside the valid 1..65535 range.</summary>
    public const string ConfigInvalidBrokerPort = "SPARKPLUG.CONFIG_INVALID_BROKER_PORT";

    /// <summary>The Sparkplug group id is empty or contains a topic-reserved character ('/', '+', '#').</summary>
    public const string ConfigInvalidGroupId = "SPARKPLUG.CONFIG_INVALID_GROUP_ID";

    /// <summary>The Sparkplug edge node id is empty or contains a topic-reserved character ('/', '+', '#').</summary>
    public const string ConfigInvalidEdgeNodeId = "SPARKPLUG.CONFIG_INVALID_EDGE_NODE_ID";

    /// <summary>The keep-alive interval is outside the MQTT 3.1.1 range (1..65535 seconds).</summary>
    public const string ConfigInvalidKeepAlive = "SPARKPLUG.CONFIG_INVALID_KEEPALIVE";

    /// <summary>A present client id is blank/whitespace (omit it to auto-generate, or supply a real id).</summary>
    public const string ConfigInvalidClientId = "SPARKPLUG.CONFIG_INVALID_CLIENT_ID";

    /// <summary>
    /// The bounded transport-recovery budget is invalid: a non-positive attempt
    /// count or initial delay, or a max delay below the initial delay (plan v3 §4.7).
    /// </summary>
    public const string ConfigInvalidRecoveryBudget = "SPARKPLUG.CONFIG_INVALID_RECOVERY_BUDGET";

    /// <summary>Broker auth is incomplete: username without password, or vice versa.</summary>
    public const string ConfigAuthIncomplete = "SPARKPLUG.CONFIG_AUTH_INCOMPLETE";

    // ==== Gateway identity store (K3 slice 2) ====

    /// <summary>
    /// The identity-state store is unreadable, corrupt, or a durable write/commit
    /// failed. Fail-closed: the caller must NOT proceed to CONNECT, and the store
    /// is NEVER silently reset to a fresh zero counter (K0 WS5).
    /// </summary>
    public const string IdentityStoreUnavailable = "SPARKPLUG.IDENTITY_STORE_UNAVAILABLE";

    /// <summary>The persisted identity-store schema version is not one this build understands (fail closed).</summary>
    public const string IdentityStoreSchemaUnsupported = "SPARKPLUG.IDENTITY_STORE_SCHEMA_UNSUPPORTED";

    /// <summary>
    /// A batch alias allocation violated a uniqueness invariant (duplicate canonical
    /// key or alias for the Edge Node) — the whole batch is rolled back (all-or-none).
    /// </summary>
    public const string AliasAllocationConflict = "SPARKPLUG.ALIAS_ALLOCATION_CONFLICT";

    // ==== Birth plan / schema (K3 slice 3) ====

    /// <summary>
    /// An application metric's source-qualified published name collides with a reserved
    /// well-known name (<c>bdSeq</c> or <c>Node Control/Rebirth</c>). Defensive: the
    /// source-qualified 3-part join structurally cannot equal either, but the birth plan
    /// validates it rather than trusting the invariant.
    /// </summary>
    public const string BirthReservedMetricName = "SPARKPLUG.BIRTH_RESERVED_METRIC_NAME";

    /// <summary>Two birth metrics resolve to the same published name (duplicate manifest entry).</summary>
    public const string BirthDuplicateMetricName = "SPARKPLUG.BIRTH_DUPLICATE_METRIC_NAME";

    /// <summary>
    /// An already-announced metric's static schema (datatype/unit/name/identity) changed —
    /// generation-changing material mutation, deferred post-K3. K3 fails closed rather than
    /// silently re-announcing or emitting a mismatched NDATA (plan v3 §5.2).
    /// </summary>
    public const string MaterialSchemaMutation = "SPARKPLUG.MATERIAL_SCHEMA_MUTATION";

    // ==== Replay / DATA lifecycle (K3 slice 5) ====

    /// <summary>
    /// A context publish (<c>PublishAsync</c>/<c>CompleteCatchUpAsync</c>) arrived with no active
    /// session (Core called the replay path before a successful Begin). A lifecycle-invariant
    /// violation — fail closed, never a retryable publish failure (plan v3 §7).
    /// </summary>
    public const string PublishNoSession = "SPARKPLUG.PUBLISH_NO_SESSION";

    /// <summary>
    /// A context publish carried a <see cref="ElpisEdgeConnect.Core.Adapters.PublishContext.SessionId"/>
    /// that is not the actor's authoritative session — a lifecycle-invariant violation (plan v3 §7).
    /// </summary>
    public const string PublishSessionMismatch = "SPARKPLUG.PUBLISH_SESSION_MISMATCH";

    /// <summary>
    /// A context publish carried an epoch that is not the actor's current successful-birth epoch —
    /// a lifecycle-invariant violation, not a retryable publish failure (plan v3 §7, §1.9).
    /// </summary>
    public const string PublishEpochMismatch = "SPARKPLUG.PUBLISH_EPOCH_MISMATCH";

    /// <summary>
    /// At catch-up cutover an announced metric was absent from the cutover snapshot — a
    /// manifest-invariant violation the actor fails closed on rather than silently dropping
    /// (plan v3 §1.5, §5.3).
    /// </summary>
    public const string ManifestInvariantViolation = "SPARKPLUG.MANIFEST_INVARIANT_VIOLATION";

    /// <summary>
    /// A DATA batch was not accepted because the actor requested a Core rebirth first (transport
    /// suspect after a failed/uncertain send, or a first-observed metric needing re-announcement).
    /// Retryable: Core processes the rebirth, then retries the same unacknowledged subrange under
    /// the newer epoch (plan v3 §4.2, §5.1).
    /// </summary>
    public const string PublishRebirthRequested = "SPARKPLUG.PUBLISH_REBIRTH_REQUESTED";
}
