// ============================================================================
// File: Configuration/BrokerEndpointTests.cs
// Covers: K1.1 — canonical BrokerEndpoint normalization + equality (equivalent
//         spellings collapse; different endpoints stay distinct).
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class BrokerEndpointTests
{
    [Fact]
    public void Case_And_Default_Port_Are_Normalized_Equal()
    {
        var a = BrokerEndpoint.Create("BROKER.EXAMPLE.COM", 1883);
        var b = BrokerEndpoint.Create("broker.example.com"); // default plain port 1883
        a.Should().Be(b);
        a.Host.Should().Be("broker.example.com");
        a.Port.Should().Be(1883);
    }

    [Fact]
    public void Tls_Uses_Default_Tls_Port_And_Differs_From_Plain()
    {
        var tls = BrokerEndpoint.Create("host");            // 1883, no TLS
        var secure = BrokerEndpoint.Create("host", tls: true); // 8883, TLS
        secure.Port.Should().Be(BrokerEndpoint.DefaultTlsPort);
        secure.Should().NotBe(tls);
    }

    [Fact]
    public void Different_Host_Or_Port_Are_Distinct()
    {
        BrokerEndpoint.Create("h1").Should().NotBe(BrokerEndpoint.Create("h2"));
        BrokerEndpoint.Create("h", 1883).Should().NotBe(BrokerEndpoint.Create("h", 1884));
    }

    [Fact]
    public void Empty_Host_Is_Rejected()
    {
        var act = () => BrokerEndpoint.Create("   ");
        act.Should().Throw<ArgumentException>();
    }

    // ---- Required test: scheme/path bypass, embedded port, and IPv6 normalization ----
    [Theory]
    [InlineData("mqtt://broker.example.com")] // scheme
    [InlineData("broker.example.com/path")]   // path
    [InlineData("broker.example.com:1883")]   // embedded port on a DNS host
    public void Scheme_Path_Or_Embedded_Port_Are_Rejected(string host)
    {
        var act = () => BrokerEndpoint.Create(host);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Equivalent_IPv6_Spellings_Normalize_Equal_And_Render_Bracketed()
    {
        var bracketed = BrokerEndpoint.Create("[0:0:0:0:0:0:0:1]", 1883);
        var bare = BrokerEndpoint.Create("::1"); // default plain port
        bracketed.Should().Be(bare);
        bracketed.Host.Should().Be("::1");
        bracketed.IsIPv6.Should().BeTrue();
        bracketed.ToString().Should().Be("mqtt://[::1]:1883"); // bracketed with port
    }

    [Fact]
    public void Invalid_Bracketed_IPv6_Is_Rejected()
    {
        var act = () => BrokerEndpoint.Create("[not-an-ipv6]");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void Out_Of_Range_Port_Is_Rejected(int port)
    {
        var act = () => BrokerEndpoint.Create("host", port);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---- Required test 4: a default BrokerEndpoint cannot enter an identity state ----
    [Fact]
    public void BrokerEndpoint_Is_A_Reference_Type_So_There_Is_No_Zero_Value_Identity_Hole()
    {
        // As a sealed reference type, `default(BrokerEndpoint)` is null — not a
        // Host=null/Port=0 struct that could compare or render as a real endpoint.
        typeof(BrokerEndpoint).IsValueType.Should().BeFalse();
        BrokerEndpoint? defaulted = default;
        defaulted.Should().BeNull();

        // The only path to an instance is the validating factory; a valid one has a non-empty host.
        var real = BrokerEndpoint.Create("host");
        real.Host.Should().NotBeNullOrEmpty();
        (real == null).Should().BeFalse();
        (real != null).Should().BeTrue();
    }
}
