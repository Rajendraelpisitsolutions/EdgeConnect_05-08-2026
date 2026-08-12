// ---------------------------------------------------------------------------
// File:    Components/Shared/ErrorGuidance.cs
// Purpose: Turn an adapter error CODE into something a maintenance engineer
//          standing at a machine can act on.
//
// Why this exists
// ---------------
// "MQTT.PERTAG_PUBLISH_FAILED — PerTag publish failed for tag 'motor_running':
// The MQTT client is disconnected" is a developer's sentence. It names the
// symptom in the vocabulary of the code that threw it and says nothing about
// what to DO. An operator's first question is not "which method failed" but
// "is it the machine, the network, or the gateway — and what do I check?"
//
// Each entry answers exactly that: one line of what it means, one line of what
// to check. The raw code is still shown (as a chip beside the timestamp) so it
// stays greppable in a support ticket, and the adapter's own message is kept as
// the fallback for any code not catalogued here — never swallowed.
//
// Platform principle P6 — operational product, not developer tool.
// ---------------------------------------------------------------------------

namespace ElpisEdgeConnect.Management.Components.Shared;

/// <summary>Operator-facing explanation of one adapter error code.</summary>
/// <param name="Meaning">What has actually happened, in plain language.</param>
/// <param name="WhatToCheck">The next physical or configuration step. Null when the meaning is self-contained.</param>
public sealed record ErrorExplanation(string Meaning, string? WhatToCheck);

/// <summary>
/// Code-to-guidance catalogue for the errors surfaced on Overview route cards.
/// </summary>
public static class ErrorGuidance
{
    // Keyed on the error code as emitted by the adapters (MODULE.CATEGORY_SUB,
    // per the CLAUDE.md naming rule). Ordinal comparison — these are wire
    // identifiers, never localised.
    private static readonly Dictionary<string, ErrorExplanation> Catalogue = new(StringComparer.Ordinal)
    {
        // ── Connectivity: the gateway cannot open a conversation at all ──
        ["MODBUS.CONNECT_FAILED"] = new(
            "Cannot reach this device.",
            "Check it is powered on, plugged in, and that its address has not changed."),

        ["MODBUS.SOCKET_ERROR"] = new(
            "The connection to this device dropped mid-read.",
            "Usually a cable, switch, or the device rebooting. Check the link first."),

        ["S7.CONNECT_FAILED"] = new(
            "Cannot reach this PLC.",
            "Check it is powered on and reachable, and that the rack and slot numbers match the CPU."),

        ["MELSEC.CONNECT_FAILED"] = new(
            "Cannot reach this PLC.",
            "Check it is powered on, and that the SLMP port is open and matches the configured port."),

        ["OPCUA.CONNECT_FAILED"] = new(
            "Cannot reach this OPC UA server.",
            "Check the endpoint URL, and that the server is running and trusts this gateway's certificate."),

        ["MTCONNECT.HTTP_ERROR"] = new(
            "The MTConnect agent did not answer.",
            "Check the agent is running and its URL is reachable from this gateway."),

        ["BROTHER.HTTP_ERROR"] = new(
            "The machine's web interface did not answer.",
            "Check the machine is powered on and reachable from this gateway."),

        // ── Gateway-side: nothing is wrong with the machine ──
        ["FOCAS2.COLLECT_ERROR"] = new(
            "A driver file this protocol needs is missing on the gateway.",
            "Nothing is wrong with the machine. Send this to Elpis support — the file has to be installed beside EdgeConnect."),

        ["FOCAS2.HANDLE_EXHAUSTED"] = new(
            "This gateway has run out of FOCAS connection handles.",
            "Too many CNC connections are open at once. Restart the gateway, or reduce the number of FOCAS sources."),

        // ── Delivery: data is read but not leaving ──
        ["MQTT.PERTAG_PUBLISH_FAILED"] = new(
            "Cannot publish to the broker, so readings are being held on disk.",
            "Check the broker is running and reachable. Nothing is lost while the buffer has room."),

        ["MQTT.CONNECT_FAILED"] = new(
            "Cannot connect to the MQTT broker.",
            "Check the broker address and port, and the username and password if the broker requires them."),

        ["MQTT.PUBLISH_FAILED"] = new(
            "The broker refused or dropped a publish.",
            "Check the broker is healthy and that this gateway is allowed to publish to the configured topic."),
    };

    /// <summary>
    /// Guidance for <paramref name="errorCode"/>, or null when the code is not
    /// catalogued. Callers fall back to the adapter's own message so an
    /// uncatalogued fault still explains itself as well as it ever did.
    /// </summary>
    public static ErrorExplanation? For(string? errorCode) =>
        errorCode is not null && Catalogue.TryGetValue(errorCode, out var explanation)
            ? explanation
            : null;

    /// <summary>
    /// What to show as the headline of an error panel: the catalogued meaning
    /// when there is one, otherwise the adapter's raw message, otherwise the
    /// code itself. Never returns empty — a panel with no words is worse than a
    /// developer's sentence.
    /// </summary>
    public static string Describe(string? errorCode, string? rawMessage)
    {
        var explanation = For(errorCode);
        if (explanation is not null)
        {
            return explanation.Meaning;
        }

        if (!string.IsNullOrWhiteSpace(rawMessage))
        {
            return rawMessage;
        }

        return errorCode ?? "Something went wrong and the adapter did not say what.";
    }
}
