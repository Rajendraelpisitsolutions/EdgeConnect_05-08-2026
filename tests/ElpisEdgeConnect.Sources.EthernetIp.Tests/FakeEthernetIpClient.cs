// ============================================================================
// Test fake for IEthernetIpClient — fully controllable, no PLC, no native
// calls. Mirrors the FakeModbusClient / S7DemoClient pattern.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ElpisEdgeConnect.Core.Model;
using ElpisEdgeConnect.Sources.EthernetIp;

namespace ElpisEdgeConnect.Sources.EthernetIp.Tests;

/// <summary>In-memory <see cref="IEthernetIpClient"/> for deterministic tests.</summary>
internal sealed class FakeEthernetIpClient : IEthernetIpClient
{
    private readonly Dictionary<string, (object Value, CanonicalValueType Type)> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _notFound = new(StringComparer.Ordinal);
    private readonly HashSet<string> _fatalTags = new(StringComparer.Ordinal);

    /// <summary>When true, <see cref="ConnectAsync"/> throws a fatal exception.</summary>
    public bool ConnectShouldFail { get; set; }

    /// <summary>Number of times ConnectAsync ran to completion.</summary>
    public int ConnectCount { get; private set; }

    /// <summary>Number of ReadTagAsync calls.</summary>
    public int ReadCount { get; private set; }

    public bool IsConnected { get; private set; }

    /// <summary>Seed a successful read value for a tag address.</summary>
    public FakeEthernetIpClient WithValue(string address, object value, CanonicalValueType type)
    {
        _values[address] = (value, type);
        return this;
    }

    /// <summary>Mark a tag address as not-found (non-fatal read failure).</summary>
    public FakeEthernetIpClient WithNotFound(string address)
    {
        _notFound.Add(address);
        return this;
    }

    /// <summary>Mark a tag address as a fatal transport failure on read.</summary>
    public FakeEthernetIpClient WithFatalRead(string address)
    {
        _fatalTags.Add(address);
        return this;
    }

    public Task ConnectAsync(EthernetIpConnectionParameters parameters, CancellationToken ct)
    {
        if (ConnectShouldFail)
        {
            IsConnected = false;
            throw new EthernetIpFatalException("ETHERNETIP.CONNECT_FAILED", "fake connect failure");
        }
        ConnectCount++;
        IsConnected = true;
        return Task.CompletedTask;
    }

    public void Disconnect() => IsConnected = false;

    public Task<EthernetIpReadResult> ReadTagAsync(string address, EthernetIpElementType elementType, CancellationToken ct)
    {
        ReadCount++;
        if (_fatalTags.Contains(address))
        {
            IsConnected = false;
            throw new EthernetIpFatalException("ETHERNETIP.READ_FAILED", $"fake fatal read for '{address}'");
        }
        if (_notFound.Contains(address))
        {
            return Task.FromResult(EthernetIpReadResult.Fail("ETHERNETIP.TAG_NOT_FOUND", $"'{address}' not found"));
        }
        if (_values.TryGetValue(address, out var v))
        {
            return Task.FromResult(EthernetIpReadResult.Ok(v.Value, v.Type));
        }
        return Task.FromResult(EthernetIpReadResult.Fail("ETHERNETIP.READ_FAILED", $"no fake value for '{address}'"));
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Settable <see cref="TimeProvider"/> for deterministic per-tag-timer tests.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
