// ============================================================================
// File: EthernetIpTagBrowser.cs
// Purpose: Read the controller's own CIP symbol table, so an operator can see
//          which addresses a device actually publishes instead of guessing one
//          at a time through Test Read.
//
//          Motivation (field incident, 11-Aug-2026): a Micro820 published only
//          its 24 built-in _IO_EM_* I/O aliases, because the project's process
//          variables were declared at program scope rather than as Global
//          Variables. With no way to list symbols, the operator spent hours
//          trying spellings, letter cases and underscore prefixes against a
//          controller that was never going to answer to any of them. One
//          listing call settles it.
//
//          Read-only: libplctag's "@tags" pseudo-tag asks the controller to
//          enumerate itself. No controller state is touched.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using libplctag;

namespace ElpisEdgeConnect.Sources.EthernetIp;

/// <summary>One symbol published by a controller's CIP symbol table.</summary>
public sealed record EthernetIpSymbol
{
    /// <summary>Symbol name exactly as the controller reports it.</summary>
    public required string Name { get; init; }

    /// <summary>Raw CIP type code (e.g. <c>0x00C1</c> = BOOL).</summary>
    public required int CipTypeCode { get; init; }

    /// <summary>Element size in bytes as reported by the controller.</summary>
    public required int ElementLength { get; init; }

    /// <summary>
    /// EdgeConnect datatype token to configure for this symbol
    /// (BOOL / SINT / INT / DINT / LINT / REAL / LREAL / STRING), or
    /// <see langword="null"/> when the CIP type has no atomic equivalent.
    /// </summary>
    public string? Datatype { get; init; }

    /// <summary>True when the CIP type is a structure/UDT rather than an atomic.</summary>
    public bool IsStructure { get; init; }

    /// <summary>
    /// True for controller-defined built-ins (the <c>_IO_EM_*</c> embedded-I/O
    /// aliases) rather than variables from the operator's own project. Lets the
    /// UI separate "what the firmware gives you" from "what your program
    /// publishes" — the distinction the incident turned on.
    /// </summary>
    public bool IsBuiltIn { get; init; }
}

/// <summary>Reads a controller's CIP symbol table. Read-only.</summary>
public interface IEthernetIpTagBrowser
{
    /// <summary>
    /// List every symbol the controller publishes. Throws
    /// <see cref="EthernetIpFatalException"/> when the controller cannot be
    /// reached or refuses the listing.
    /// </summary>
    Task<IReadOnlyList<EthernetIpSymbol>> ListTagsAsync(
        EthernetIpConnectionParameters parameters, CancellationToken ct);
}

/// <summary>libplctag-backed <see cref="IEthernetIpTagBrowser"/>.</summary>
public sealed class LibPlcTagTagBrowser : IEthernetIpTagBrowser
{
    /// <summary>
    /// libplctag's tag-listing pseudo-tag. Supported by Logix and Micro800
    /// families; older PLC5 / SLC500 controllers have no symbol table and will
    /// refuse it.
    /// </summary>
    private const string ListingTagName = "@tags";

    /// <summary>Entry header: instance id (4) + type (2) + length (2) + dims (12) + name length (2).</summary>
    private const int EntryHeaderBytes = 22;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EthernetIpSymbol>> ListTagsAsync(
        EthernetIpConnectionParameters parameters, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(parameters.Host))
        {
            throw new EthernetIpFatalException(
                EthernetIpErrors.ConfigInvalid, "EtherNet/IP host is required.");
        }

        using var tag = new Tag
        {
            Name = ListingTagName,
            Gateway = parameters.Host,
            Path = string.IsNullOrWhiteSpace(parameters.Path) ? null : parameters.Path,
            PlcType = MapPlcType(parameters.CpuFamily),
            Protocol = Protocol.ab_eip,
            Timeout = parameters.RequestTimeout,
        };

        try
        {
            await tag.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (LibPlcTagException ex)
        {
            throw new EthernetIpFatalException(
                EthernetIpErrors.ReadFailed,
                $"Could not list tags on {parameters.Host}: {ex.Message}. "
                    + "Not every controller family publishes a symbol table.",
                ex);
        }

        var size = tag.GetSize();
        var buffer = new byte[size];
        for (var i = 0; i < size; i++)
        {
            buffer[i] = tag.GetUInt8(i);
        }

        return Decode(buffer);
    }

    /// <summary>
    /// Decode the packed listing payload. Exposed for tests so the wire format
    /// is pinned without a controller.
    /// </summary>
    internal static IReadOnlyList<EthernetIpSymbol> Decode(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        var symbols = new List<EthernetIpSymbol>();
        var offset = 0;

        while (offset + EntryHeaderBytes <= buffer.Length)
        {
            offset += 4;                                                    // instance id
            var typeCode = BitConverter.ToUInt16(buffer, offset); offset += 2;
            var elementLength = BitConverter.ToUInt16(buffer, offset); offset += 2;
            offset += 12;                                                   // array dimensions
            var nameLength = BitConverter.ToUInt16(buffer, offset); offset += 2;

            // A zero-length or overrunning name means we have walked off the end
            // of the meaningful payload — stop rather than emit garbage.
            if (nameLength == 0 || offset + nameLength > buffer.Length)
            {
                break;
            }

            var name = Encoding.ASCII.GetString(buffer, offset, nameLength);
            offset += nameLength;

            // Bit 15 marks a structure/UDT; the low bits are then a template id
            // rather than an atomic type code.
            var isStructure = (typeCode & 0x8000) != 0;

            symbols.Add(new EthernetIpSymbol
            {
                Name = name,
                CipTypeCode = typeCode,
                ElementLength = elementLength,
                IsStructure = isStructure,
                Datatype = isStructure ? null : MapDatatype(typeCode),
                IsBuiltIn = name.StartsWith("_IO_EM_", StringComparison.Ordinal)
                    || name.StartsWith("__SYSVA_", StringComparison.Ordinal),
            });
        }

        return symbols;
    }

    /// <summary>
    /// Map a CIP atomic type code onto the datatype token the operator must
    /// configure. Unsigned types map onto the next signed type EdgeConnect
    /// supports, which is why UINT surfaces as INT.
    /// </summary>
    internal static string? MapDatatype(int cipTypeCode) => cipTypeCode switch
    {
        0x00C1 => "BOOL",
        0x00C2 => "SINT",   // SINT
        0x00C6 => "SINT",   // USINT
        0x00C3 => "INT",    // INT
        0x00C7 => "INT",    // UINT
        0x00C4 => "DINT",   // DINT
        0x00C8 => "DINT",   // UDINT
        0x00C5 => "LINT",   // LINT
        0x00C9 => "LINT",   // ULINT
        0x00CA => "REAL",
        0x00CB => "LREAL",
        0x00D0 or 0x08FCE => "STRING",
        _ => null,
    };

    private static PlcType MapPlcType(EthernetIpCpuFamily family) => family switch
    {
        EthernetIpCpuFamily.ControlLogix => PlcType.ControlLogix,
        EthernetIpCpuFamily.CompactLogix => PlcType.ControlLogix,
        EthernetIpCpuFamily.GuardLogix => PlcType.ControlLogix,
        EthernetIpCpuFamily.MicroLogix => PlcType.MicroLogix,
        EthernetIpCpuFamily.Micro800 => PlcType.Micro800,
        EthernetIpCpuFamily.Slc500 => PlcType.Slc500,
        EthernetIpCpuFamily.Plc5 => PlcType.Plc5,
        _ => PlcType.ControlLogix,
    };
}
