// ============================================================================
// File: MelsecDatatype.cs
// Purpose: Slice-1 read datatypes and a string-hint parser. Unknown hints are
//          rejected explicitly (typed error) rather than guessed. Naming follows
//          Mitsubishi / IEC vocabulary (INT = 16-bit, DINT = 32-bit, WORD = u16,
//          DWORD = u32, REAL = float32).
// ============================================================================

using System;

namespace ElpisEdgeConnect.Sources.Melsec;

/// <summary>Datatypes the Slice-1 decoder can produce.</summary>
public enum MelsecDatatype
{
    /// <summary>Single bit (from a word-bit address or a bit device).</summary>
    Bool,

    /// <summary>Signed 16-bit (one word).</summary>
    Int16,

    /// <summary>Unsigned 16-bit (one word).</summary>
    UInt16,

    /// <summary>Signed 32-bit (two words, word-order applied).</summary>
    Int32,

    /// <summary>Unsigned 32-bit (two words, word-order applied).</summary>
    UInt32,

    /// <summary>IEEE-754 single-precision float (two words, word-order applied).</summary>
    Float32,
}

/// <summary>Parses operator datatype hints into <see cref="MelsecDatatype"/>.</summary>
public static class MelsecDatatypeParser
{
    /// <summary>
    /// Parse a datatype hint. Returns false with a reason for an empty or
    /// unrecognized hint — never guesses.
    /// </summary>
    public static bool TryParse(string? text, out MelsecDatatype datatype, out string? error)
    {
        datatype = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "datatype hint is empty";
            return false;
        }

        switch (text.Trim().ToLowerInvariant())
        {
            case "bool":
            case "bit":
            case "boolean":
                datatype = MelsecDatatype.Bool; break;
            case "int16":
            case "int":
            case "short":
                datatype = MelsecDatatype.Int16; break;
            case "uint16":
            case "word":
            case "ushort":
                datatype = MelsecDatatype.UInt16; break;
            case "int32":
            case "dint":
                datatype = MelsecDatatype.Int32; break;
            case "uint32":
            case "udint":
            case "dword":
                datatype = MelsecDatatype.UInt32; break;
            case "float32":
            case "real":
            case "float":
            case "single":
                datatype = MelsecDatatype.Float32; break;
            default:
                error = $"unsupported datatype '{text}'";
                return false;
        }

        error = null;
        return true;
    }
}
