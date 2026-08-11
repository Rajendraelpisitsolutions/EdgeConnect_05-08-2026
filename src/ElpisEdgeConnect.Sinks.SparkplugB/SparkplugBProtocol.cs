// ============================================================================
// File: SparkplugBProtocol.cs
// Purpose: Protocol identity constants for the Sparkplug B sink assembly.
// Implements ADR-0035 Rule 1 (LOCKED architectural decision): Sparkplug B is a
// separate, coexisting sink assembly with ProtocolName "sparkplug-b" — never a
// mode of the MQTT sink, and never a change to the EREMOS topic contract.
// Reference: docs/decisions/0035-sparkplug-b-northbound-standard.md;
//            docs/sessions/2026-07-19-sparkplug-b-k2-wire-payload-plan-v3.md.
// ============================================================================

namespace ElpisEdgeConnect.Sinks.SparkplugB;

/// <summary>
/// Protocol identity constants for the Sparkplug B sink (ADR-0035 Rule 1).
/// </summary>
public static class SparkplugBProtocol
{
    /// <summary>The protocol name used in configuration, registration, and licensing.</summary>
    public const string ProtocolName = "sparkplug-b";

    /// <summary>
    /// The Sparkplug topic-namespace token (<c>spBv1.0</c>). This is the namespace
    /// element of the topic grammar, not the specification revision — the normative
    /// specification is Eclipse Sparkplug 3.0.0 (see the conformance note).
    /// </summary>
    public const string TopicNamespace = "spBv1.0";
}
