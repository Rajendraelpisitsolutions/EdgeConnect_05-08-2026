// ============================================================================
// File: Identity/SparkplugSequenceNumber.cs
// Purpose: Constrained seq domain type (plan v2 §3.5, review amendment A): the
//          Sparkplug message sequence number is 0..255. K3 manages the counter
//          and its wrap; K2 merely makes an invalid wire value unconstructible.
//          NBIRTH carries a seq; NDEATH never does (the NDEATH factory takes
//          no seq at all).
// Reference: spec seq requirements (payloads ch. 6); plan v3 (frozen) §1, §3.2.
// ============================================================================

namespace ElpisEdgeConnect.Sinks.SparkplugB.Identity;

/// <summary>
/// A validated Sparkplug message sequence number (0..255). Built via
/// <see cref="Create"/>; the wrap-after-255 policy belongs to the K3 session
/// actor, not this type.
/// </summary>
public readonly record struct SparkplugSequenceNumber
{
    private SparkplugSequenceNumber(byte value) => Value = value;

    /// <summary>The sequence value (0..255 by construction).</summary>
    public byte Value { get; }

    /// <summary>The value as encoded on the wire (uint64 varint field).</summary>
    public ulong WireValue => Value;

    /// <summary>Create a validated sequence number.</summary>
    /// <param name="value">The value; must be within 0..255.</param>
    /// <returns>The sequence number.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is outside 0..255.</exception>
    public static SparkplugSequenceNumber Create(int value)
    {
        if (value is < 0 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A Sparkplug sequence number must be within 0..255.");
        }

        return new SparkplugSequenceNumber((byte)value);
    }
}
