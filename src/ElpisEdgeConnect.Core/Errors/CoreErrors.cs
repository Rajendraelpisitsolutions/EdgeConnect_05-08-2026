// ============================================================================
// File: Errors/CoreErrors.cs
// Purpose: Stable error code catalog for the Core module. Every error code
//          thrown from Core subsystems must be defined here so they can be
//          documented, translated, and reasoned about by diagnostics.
// Reference: ARCHITECTURE_BLUEPRINT.md Section 13.3
//
// NAMING CONVENTION (locked):
//   CORE.{CATEGORY}_{SUBCATEGORY}
//   - CATEGORY is one of: CONFIG, LICENSE, ROUTE, BUFFER, PIPELINE, RUNTIME
//   - SUBCATEGORY is a specific failure mode in SCREAMING_SNAKE_CASE
//
// Adding a new error code is allowed during implementation. Removing or
// renaming an existing code is a breaking change.
// ============================================================================

namespace ElpisEdgeConnect.Core.Errors;

/// <summary>
/// Stable catalog of Core error codes. String constants rather than an enum
/// so that protocol modules can define their own code spaces
/// (e.g., <c>FOCAS2.HANDLE_EXHAUSTED</c>) without touching Core.
/// </summary>
public static class CoreErrors
{
    // ==== Configuration ====

    /// <summary>
    /// Generic configuration error when a more specific code does not apply.
    /// Prefer one of the specific <c>Config*</c> codes below when possible.
    /// </summary>
    public const string ConfigInvalid = "CORE.CONFIG_INVALID";

    /// <summary>A required field is missing from the configuration.</summary>
    public const string ConfigMissingField = "CORE.CONFIG_MISSING_FIELD";

    /// <summary>A field is present but its value is outside the allowed range or set.</summary>
    public const string ConfigInvalidValue = "CORE.CONFIG_INVALID_VALUE";

    /// <summary>The configuration file referenced in startup or draft apply does not exist.</summary>
    public const string ConfigFileNotFound = "CORE.CONFIG_FILE_NOT_FOUND";

    /// <summary>
    /// The configuration file exists but cannot be parsed (invalid JSON,
    /// truncated, binary garbage, etc.).
    /// </summary>
    public const string ConfigFileCorrupt = "CORE.CONFIG_FILE_CORRUPT";

    /// <summary>
    /// Two concurrent <c>ApplyDraftAsync</c> calls raced. The loser sees this
    /// code; the winner succeeds. The caller may retry after reloading the
    /// current config.
    /// </summary>
    public const string ConfigApplyConflict = "CORE.CONFIG_APPLY_CONFLICT";

    /// <summary>
    /// Draft validation failed. Attached <see cref="AdapterError.Context"/>
    /// typically carries the list of validation issues.
    /// </summary>
    public const string ConfigValidationFailed = "CORE.CONFIG_VALIDATION_FAILED";

    /// <summary>
    /// A rollback to a prior configuration version failed to apply cleanly.
    /// This is a serious condition and typically requires manual recovery.
    /// </summary>
    public const string ConfigRollbackFailed = "CORE.CONFIG_ROLLBACK_FAILED";

    /// <summary>
    /// Two source instances share the same <c>InstanceId</c>. Source ids
    /// must be unique within the gateway. Cross-record rule from B2.
    /// </summary>
    public const string ConfigDuplicateSourceId = "CORE.CONFIG_DUPLICATE_SOURCE_ID";

    /// <summary>
    /// Two sink instances share the same <c>InstanceId</c>. Sink ids
    /// must be unique within the gateway. Cross-record rule from B2.
    /// </summary>
    public const string ConfigDuplicateSinkId = "CORE.CONFIG_DUPLICATE_SINK_ID";

    /// <summary>
    /// Two routes share the same <c>RouteId</c>. Route ids must be unique
    /// within the gateway. Cross-record rule from B2.
    /// </summary>
    public const string ConfigDuplicateRouteId = "CORE.CONFIG_DUPLICATE_ROUTE_ID";

    /// <summary>
    /// A <c>TransformProfile.TagMapping</c> entry has an invalid value
    /// (null, empty, or whitespace target). Cross-record rule from B2.
    /// </summary>
    public const string ConfigInvalidTagMapping = "CORE.CONFIG_INVALID_TAG_MAPPING";

    /// <summary>
    /// Two or more <em>enabled</em> source instances are configured against the
    /// same device endpoint (same PLC/CNC host and port, same serial line, same
    /// OPC UA endpoint URL, …). Each source opens its own connection, and many
    /// controllers cap concurrent connections at one or two, so the duplicate
    /// silently halves the budget and typically surfaces later as an
    /// intermittent connection refusal on a <em>different</em> source.
    /// <para>
    /// This is issued as a <b>warning</b>, never an error: reading different
    /// register ranges from one PLC at different poll rates is a legitimate
    /// pattern, and a configuration that works today must not stop applying.
    /// Cross-record rule 13.
    /// </para>
    /// </summary>
    public const string ConfigDuplicateSourceEndpoint = "CORE.CONFIG_DUPLICATE_SOURCE_ENDPOINT";

    /// <summary>
    /// The configuration audit log file failed integrity verification.
    /// The hash chain has been broken at some entry, indicating tampering
    /// or filesystem corruption. The first broken entry index appears in
    /// the error <see cref="AdapterError.Context"/>.
    /// </summary>
    public const string ConfigAuditCorrupt = "CORE.CONFIG_AUDIT_CORRUPT";

    /// <summary>
    /// The gateway data root is not writable by the current process identity —
    /// e.g. the audit log or the active configuration file cannot be appended to
    /// or replaced (typically Windows <c>ProgramData</c> files created under an
    /// elevated context, leaving a non-admin runtime user read-only). The data
    /// pipeline keeps running (fail-soft), but audit-writing operations — config
    /// apply/rollback and diagnostic-bundle generation — will fail until the data
    /// root is made writable. The unwritable path(s) appear in the fault message.
    /// </summary>
    public const string ConfigDataNotWritable = "CORE.CONFIG_DATA_NOT_WRITABLE";

    // ==== Licensing ====

    /// <summary>The license file configured for this gateway does not exist on disk.</summary>
    public const string LicenseFileNotFound = "CORE.LICENSE_FILE_NOT_FOUND";

    /// <summary>
    /// The license file exists but its RSA signature is invalid or does not
    /// match the embedded public key. May indicate tampering or a wrong-key
    /// license file.
    /// </summary>
    public const string LicenseSignatureInvalid = "CORE.LICENSE_SIGNATURE_INVALID";

    /// <summary>
    /// The license is past its expiration date. The gateway continues to
    /// flow data per blueprint §7.4 but rejects configuration changes.
    /// </summary>
    public const string LicenseExpired = "CORE.LICENSE_EXPIRED";

    /// <summary>
    /// The license grace period (30 days past expiration) has been exhausted.
    /// All configuration changes are blocked until the license is renewed.
    /// </summary>
    public const string LicenseGraceExhausted = "CORE.LICENSE_GRACE_EXHAUSTED";

    /// <summary>
    /// A requested protocol module is not enabled by the current license
    /// (e.g., Modbus requested on a Starter license that only includes FOCAS2
    /// and MT-LINKi).
    /// </summary>
    public const string LicenseModuleDisabled = "CORE.LICENSE_MODULE_DISABLED";

    /// <summary>
    /// Configuring this instance would exceed the license's maximum number
    /// of connector instances (either overall or for this specific module).
    /// </summary>
    public const string LicenseInstanceLimitReached = "CORE.LICENSE_INSTANCE_LIMIT_REACHED";

    /// <summary>
    /// The license's <c>gatewayId</c> field does not match this gateway's
    /// identity. Prevents a license from being moved between gateways.
    /// </summary>
    public const string LicenseGatewayMismatch = "CORE.LICENSE_GATEWAY_MISMATCH";

    /// <summary>
    /// The license file exists but is structurally corrupt — missing required
    /// fields, malformed JSON, or values outside the allowed set.
    /// </summary>
    public const string LicenseFileCorrupt = "CORE.LICENSE_FILE_CORRUPT";

    /// <summary>
    /// A configuration was submitted while no license has been loaded.
    /// Empty configurations are still allowed (first-boot bootstrapping); any
    /// configuration containing sources, sinks, or routes is rejected.
    /// </summary>
    public const string LicenseNotLoaded = "CORE.LICENSE_NOT_LOADED";

    /// <summary>
    /// A configuration would exceed one of the global instance limits in the
    /// active license (<c>maxSourceInstances</c>, <c>maxSinkInstances</c>,
    /// <c>maxRoutes</c>).
    /// </summary>
    public const string LicenseGlobalLimitExceeded = "CORE.LICENSE_GLOBAL_LIMIT_EXCEEDED";

    // ==== Routing ====

    /// <summary>A route references a source instance id that does not exist in the current configuration.</summary>
    public const string RouteSourceNotFound = "CORE.ROUTE_SOURCE_NOT_FOUND";

    /// <summary>A route references a sink instance id that does not exist in the current configuration.</summary>
    public const string RouteSinkNotFound = "CORE.ROUTE_SINK_NOT_FOUND";

    /// <summary>
    /// A transform step in the route's pipeline threw an exception. The
    /// routing engine skips the failing batch and records a diagnostic event.
    /// </summary>
    public const string RouteTransformFailed = "CORE.ROUTE_TRANSFORM_FAILED";

    /// <summary>
    /// Delivery to at least one sink in the route failed after all retries
    /// were exhausted. Buffer retention keeps the data for later recovery.
    /// </summary>
    public const string RouteDeliveryFailed = "CORE.ROUTE_DELIVERY_FAILED";

    /// <summary>
    /// A route-state transition was requested that is not legal per the
    /// lifecycle defined in blueprint §19.9.
    /// </summary>
    public const string RouteInvalidState = "CORE.ROUTE_INVALID_STATE";

    /// <summary>
    /// A route is being registered with the routing engine but a route with
    /// the same id is already registered. Caller must unregister first.
    /// (C3 commit 1.)
    /// </summary>
    public const string RouteAlreadyRegistered = "CORE.ROUTE_ALREADY_REGISTERED";

    /// <summary>
    /// An operation referenced a route id that is not registered with the
    /// routing engine. (C3 commit 1.)
    /// </summary>
    public const string RouteNotFound = "CORE.ROUTE_NOT_FOUND";

    /// <summary>
    /// A route lifecycle transition was attempted that is not permitted by
    /// the blueprint §19.9 transition table. Distinct from
    /// <see cref="RouteInvalidState"/>: this code is the specific transition
    /// rejection (e.g., <c>Stopped → Running</c>), not a general state error.
    /// (C3 commit 1.)
    /// </summary>
    public const string RouteInvalidLifecycleTransition = "CORE.ROUTE_INVALID_LIFECYCLE_TRANSITION";

    /// <summary>
    /// A route's <see cref="Configuration.DeliveryPolicyConfig"/> has
    /// <c>MaxBackoffMs &lt; InitialBackoffMs</c>. Cross-record rule 12 from
    /// C3 commit 1.
    /// </summary>
    public const string RouteBackoffRangeInvalid = "CORE.ROUTE_BACKOFF_RANGE_INVALID";

    /// <summary>
    /// The route's store-and-forward buffer has hit its capacity ceiling
    /// and the drop policy has started discarding points.
    /// </summary>
    public const string RouteBufferExhausted = "CORE.ROUTE_BUFFER_EXHAUSTED";

    /// <summary>
    /// A route has an empty <c>SinkInstanceIds</c> list. Every route must
    /// dispatch to at least one sink. Cross-record rule from B2.
    /// </summary>
    public const string RouteNoSinks = "CORE.ROUTE_NO_SINKS";

    /// <summary>
    /// A route uses <see cref="Configuration.DeliveryMode.AtMostOnce"/> with
    /// a buffer mode other than <see cref="Configuration.BufferMode.None"/>.
    /// Per blueprint §19.7, AtMostOnce bypasses the buffer entirely and is
    /// only valid with BufferMode.None. Cross-record rule from B2.
    /// </summary>
    public const string RouteAtMostOnceRequiresNoBuffer = "CORE.ROUTE_ATMOSTONCE_REQUIRES_NO_BUFFER";

    /// <summary>
    /// A route uses <see cref="Configuration.BufferMode.StoreAndForward"/> with
    /// a delivery mode other than <see cref="Configuration.DeliveryMode.AtLeastOnce"/>.
    /// Per blueprint §6, store-and-forward requires AtLeastOnce semantics.
    /// Cross-record rule from B2.
    /// </summary>
    public const string RouteStoreAndForwardRequiresAtLeastOnce = "CORE.ROUTE_STOREANDFORWARD_REQUIRES_ATLEASTONCE";

    /// <summary>
    /// A route has a replay-aware sink (<c>IReplayAwareSinkAdapter</c>) alongside another sink.
    /// A replay-aware sink owns the protected replay cursor and cannot share a route with an
    /// ordinary sink — a replay route is exactly one sink (K1.3, ADR-0036).
    /// </summary>
    public const string ReplayRouteRequiresSingleSink = "CORE.REPLAY_ROUTE_REQUIRES_SINGLE_SINK";

    /// <summary>
    /// A route with a replay-aware sink uses a buffer mode other than
    /// <see cref="Configuration.BufferMode.StoreAndForward"/>. Replay routes require durable
    /// store-and-forward (no depth eviction) — <c>None</c>/<c>InMemory</c> are rejected (K1.3).
    /// </summary>
    public const string ReplayRouteRequiresStoreAndForward = "CORE.REPLAY_ROUTE_REQUIRES_STORE_AND_FORWARD";

    /// <summary>
    /// A route with a replay-aware sink resolved a buffer that does not implement the replay
    /// capability (<c>IReplayRouteBuffer</c>). The buffer cannot supply the tracked-append /
    /// capture surface a replay session needs (K1.3).
    /// </summary>
    public const string ReplayRouteBufferNotCapable = "CORE.REPLAY_ROUTE_BUFFER_NOT_CAPABLE";

    /// <summary>
    /// An ordinary (non-replay) route resolved a buffer whose store is already
    /// replay-tracking-enabled. Silently downgrading to the legacy enqueue path would fail
    /// later when the enabled store rejects it — so registration fails closed (K1.3, no
    /// automatic downgrade).
    /// </summary>
    public const string ReplayRouteDowngradeNotAllowed = "CORE.REPLAY_ROUTE_DOWNGRADE_NOT_ALLOWED";

    /// <summary>
    /// A replay activation returned an incomplete capability set (a null provider) despite
    /// success. An internal invariant failure — NOT a legacy fallback (K1.3).
    /// </summary>
    public const string ReplayRouteIncompleteActivation = "CORE.REPLAY_ROUTE_INCOMPLETE_ACTIVATION";

    // ==== Buffer ====

    /// <summary>
    /// Low-level I/O failure while reading or writing the SQLite buffer
    /// file (disk full, permission denied, hardware error).
    /// </summary>
    public const string BufferIoError = "CORE.BUFFER_IO_ERROR";

    /// <summary>
    /// Buffer file is corrupt and cannot be recovered via WAL rollback.
    /// The file is quarantined and a fresh buffer is created.
    /// </summary>
    public const string BufferCorrupt = "CORE.BUFFER_CORRUPT";

    /// <summary>
    /// Buffer size or age exceeds the configured maximum and the drop
    /// policy is actively evicting points.
    /// </summary>
    public const string BufferCapacityExceeded = "CORE.BUFFER_CAPACITY_EXCEEDED";

    /// <summary>
    /// A per-sink cursor is ahead of the buffer write head, which should
    /// be impossible under correct operation. Indicates a serious bug.
    /// </summary>
    public const string BufferCursorInconsistent = "CORE.BUFFER_CURSOR_INCONSISTENT";

    /// <summary>
    /// A durable buffer file's persisted schema version does not match the
    /// version this binary knows how to read. The host must migrate or
    /// reject the file.
    /// </summary>
    public const string BufferSchemaMismatch = "CORE.BUFFER_SCHEMA_MISMATCH";

    /// <summary>
    /// A per-route SQLite store (<c>SqliteRouteStore</c>) could not acquire the
    /// exclusive ownership lock for its database file because another store instance
    /// in this process (or another process) already holds it. One route database has
    /// exactly one owning store; a second owner is refused rather than allowed to
    /// run a competing reclaim loop / connection set over the same file. (K1.2a.)
    /// </summary>
    public const string RouteStoreAlreadyOwned = "CORE.ROUTE_STORE_ALREADY_OWNED";

    /// <summary>
    /// Replay-state tracking cannot be activated because the route store is not fully
    /// drained — it still holds buffered points, or a sink cursor is behind the head.
    /// Activation is a drained-store transition (it fences out pre-activation backlog
    /// rather than birthing an empty manifest then replaying unannounced history); the
    /// route must be drained first, or a fresh store used. (K1.2b.)
    /// </summary>
    public const string RouteStoreReplayActivationBacklogPending = "CORE.ROUTE_STORE_REPLAY_ACTIVATION_BACKLOG_PENDING";

    /// <summary>
    /// The legacy context-free <c>EnqueueAsync</c> path was called on a store that has
    /// replay-state tracking enabled. Once enabled, appends must go through the
    /// generation-aware append path so the latest-value manifest and next_sequence stay
    /// coherent; the legacy path would bypass them. An internal invariant violation. (K1.2b.)
    /// </summary>
    public const string RouteStoreLegacyAppendOnEnabledStore = "CORE.ROUTE_STORE_LEGACY_APPEND_ON_ENABLED_STORE";

    /// <summary>
    /// The route id supplied to a route-store operation does not match the id the store
    /// was opened with / persisted in <c>meta.route_id</c>. Fails closed. (K1.2b.)
    /// </summary>
    public const string RouteStoreRouteMismatch = "CORE.ROUTE_STORE_ROUTE_MISMATCH";

    /// <summary>
    /// A re-activation supplied a different replay sink id than the one the store was
    /// originally activated with. Replacement-sink semantics are locked with K1.3; for
    /// now a mismatch is refused rather than silently treated as the same activation.
    /// </summary>
    public const string RouteStoreReplaySinkMismatch = "CORE.ROUTE_STORE_REPLAY_SINK_MISMATCH";

    /// <summary>
    /// A generation transition (or other tracked operation) was requested on a store that
    /// does not have replay-state tracking enabled. (K1.2b.)
    /// </summary>
    public const string RouteStoreReplayTrackingNotEnabled = "CORE.ROUTE_STORE_REPLAY_TRACKING_NOT_ENABLED";

    /// <summary>
    /// A generation transition named a sink whose cursor does not exist in the store. (K1.2b.)
    /// </summary>
    public const string RouteStoreSinkCursorNotFound = "CORE.ROUTE_STORE_SINK_CURSOR_NOT_FOUND";

    /// <summary>
    /// Deregistering the designated replay sink of an enabled store is refused: it would
    /// strip generation fencing of its authoritative cursor. Replacement-sink semantics
    /// are locked with K1.3. (K1.2b.)
    /// </summary>
    public const string RouteStoreReplaySinkProtected = "CORE.ROUTE_STORE_REPLAY_SINK_PROTECTED";

    /// <summary>
    /// A generation transition was refused because the named sink's cursor has not caught
    /// up to the append head (<c>cursor != next_sequence</c>). Advancing the generation
    /// while old-generation backlog is unconsumed would let it replay after the new
    /// birth. The route must drain the sink to the head first. (K1.2b, plan v3 §4a.)
    /// </summary>
    public const string RouteStoreGenerationBacklogPending = "CORE.ROUTE_STORE_GENERATION_BACKLOG_PENDING";

    /// <summary>
    /// A generation transition's expected-current value does not match the persisted
    /// current generation (a stale/racing writer). Compare-and-set fails closed. (K1.2b.)
    /// </summary>
    public const string RouteStoreStaleGeneration = "CORE.ROUTE_STORE_STALE_GENERATION";

    /// <summary>
    /// A generation transition is invalid: the next value is not <c>current + 1</c>, is
    /// negative, overflows, or the persisted generation is malformed. Fails closed. (K1.2b.)
    /// </summary>
    public const string RouteStoreGenerationInvalid = "CORE.ROUTE_STORE_GENERATION_INVALID";

    /// <summary>
    /// A <c>latest_value</c> envelope could not be encoded or decoded: an unknown codec
    /// version, an unknown value/static-property discriminator, a malformed field, an
    /// unsupported <see cref="Model.CanonicalValueType"/> (Null/Array/Object), or an
    /// envelope↔column disagreement (datatype or metric identity). Fails closed — a
    /// manifest row is never silently repaired or dropped. (K1.2c, plan v3 §5 / v3.1 §5.)
    /// </summary>
    public const string RouteStoreEnvelopeUnsupported = "CORE.ROUTE_STORE_ENVELOPE_UNSUPPORTED";

    // ==== Pipeline / Transforms ====

    /// <summary>A transform step threw an unhandled exception during <c>Apply</c>.</summary>
    public const string PipelineStepFailed = "CORE.PIPELINE_STEP_FAILED";

    /// <summary>
    /// A transform step was configured with invalid parameters (e.g., a
    /// deadband step with a negative threshold).
    /// </summary>
    public const string PipelineInvalidConfig = "CORE.PIPELINE_INVALID_CONFIG";

    /// <summary>
    /// A deadband threshold is invalid — a negative or non-finite absolute
    /// threshold, or a percentage threshold outside <c>(0, 1]</c>.
    /// </summary>
    public const string PipelineInvalidDeadbandThreshold = "CORE.PIPELINE_INVALID_DEADBAND_THRESHOLD";

    /// <summary>
    /// A rate-limit threshold is invalid — zero or negative milliseconds.
    /// </summary>
    public const string PipelineInvalidRateLimit = "CORE.PIPELINE_INVALID_RATE_LIMIT";

    /// <summary>
    /// The same tag appears in both <c>Deadband</c> and <c>DeadbandPercent</c>
    /// maps of a transform profile. A tag may only have one deadband mode.
    /// </summary>
    public const string PipelineDeadbandConflict = "CORE.PIPELINE_DEADBAND_CONFLICT";

    /// <summary>
    /// A filter glob pattern is invalid (empty, whitespace, or contains
    /// unsupported tokens).
    /// </summary>
    public const string PipelineInvalidFilterPattern = "CORE.PIPELINE_INVALID_FILTER_PATTERN";

    // ==== Runtime ====

    /// <summary>
    /// An unexpected internal error in the runtime. Indicates a bug in
    /// Core and should be reported.
    /// </summary>
    public const string RuntimeInternalError = "CORE.RUNTIME_INTERNAL_ERROR";

    /// <summary>
    /// A runtime operation was attempted before the runtime was initialized
    /// (e.g., calling the routing engine before <c>Host.StartAsync</c> ran).
    /// </summary>
    public const string RuntimeNotInitialized = "CORE.RUNTIME_NOT_INITIALIZED";

    /// <summary>
    /// An adapter state transition was attempted that is not allowed by
    /// <see cref="Adapters.AdapterStateTransitions"/>.
    /// </summary>
    public const string RuntimeInvalidStateTransition = "CORE.RUNTIME_INVALID_STATE_TRANSITION";
}
