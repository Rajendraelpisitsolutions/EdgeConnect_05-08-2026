// ============================================================================
// File: Identity/SparkplugBirthDeathSequence.cs
// Purpose: Constrained bdSeq domain type (plan v2 §3.5, review amendment A):
//          the birth/death sequence is 0..255 and wraps after 255
//          ([tck-id-message-flow-edge-node-birth-publish-will-message-payload-bdSeq]).
//          K3 owns reservation/increment (the SQLite identity store, K0 WS5);
//          K2 merely makes an invalid wire value unconstructible. The SAME
//          value must appear in the NDEATH Will payload and the paired NBIRTH.
// Reference: plan v3 (frozen) §1; K0 findings WS5.
// ============================================================================

namespace ElpisEdgeConnect.Sinks.SparkplugB.Identity;

/// <summary>
/// A validated Sparkplug birth/death sequence value (0..255). Built via
/// <see cref="Create"/>; reservation, increment, and wrap belong to the K3
/// identity store, not this type.
/// </summary>
public readonly record struct SparkplugBirthDeathSequence
{
    private SparkplugBirthDeathSequence(byte value) => Value = value;

    /// <summary>The bdSeq value (0..255 by construction).</summary>
    public byte Value { get; }

    /// <summary>The value as encoded in the bdSeq metric's Int64 arm.</summary>
    public ulong WireValue => Value;

    /// <summary>Create a validated bdSeq value.</summary>
    /// <param name="value">The value; must be within 0..255.</param>
    /// <returns>The bdSeq value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is outside 0..255.</exception>
    public static SparkplugBirthDeathSequence Create(int value)
    {
        if (value is < 0 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A Sparkplug bdSeq value must be within 0..255.");
        }

        return new SparkplugBirthDeathSequence((byte)value);
    }
}
