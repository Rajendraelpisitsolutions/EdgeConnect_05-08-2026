// ============================================================================
// File: Configuration/EnumDeserializationTests.cs
// Covers: String round-trip for BufferMode, DropPolicy, DeliveryMode.
//
// LOCKED INVARIANT TESTS: This file contains the structural pin for blueprint
// §19.7 — DeliveryMode must NOT contain an ExactlyOnce member, and the
// string "ExactlyOnce" must NOT deserialize. These two tests together prevent
// any reintroduction of ExactlyOnce delivery in v1.
// ============================================================================

using System;
using System.Linq;
using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Configuration;

public sealed class EnumDeserializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ========================================================================
    // BufferMode round-trip
    // ========================================================================

    [Theory]
    [InlineData("None", BufferMode.None)]
    [InlineData("InMemory", BufferMode.InMemory)]
    [InlineData("StoreAndForward", BufferMode.StoreAndForward)]
    public void BufferMode_DeserializesFromString(string text, BufferMode expected)
    {
        var quoted = $"\"{text}\"";
        var actual = JsonSerializer.Deserialize<BufferMode>(quoted, JsonOptions);
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData(BufferMode.None, "None")]
    [InlineData(BufferMode.InMemory, "InMemory")]
    [InlineData(BufferMode.StoreAndForward, "StoreAndForward")]
    public void BufferMode_SerializesToString(BufferMode value, string expected)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        json.Should().Be($"\"{expected}\"");
    }

    [Fact]
    public void BufferMode_UnknownValue_Throws()
    {
        var act = () => JsonSerializer.Deserialize<BufferMode>("\"AbsoluteNonsense\"", JsonOptions);
        act.Should().Throw<JsonException>();
    }

    // ========================================================================
    // DropPolicy round-trip
    // ========================================================================

    [Theory]
    [InlineData("DropOldest", DropPolicy.DropOldest)]
    [InlineData("DropNewest", DropPolicy.DropNewest)]
    [InlineData("Block", DropPolicy.Block)]
    public void DropPolicy_DeserializesFromString(string text, DropPolicy expected)
    {
        var quoted = $"\"{text}\"";
        var actual = JsonSerializer.Deserialize<DropPolicy>(quoted, JsonOptions);
        actual.Should().Be(expected);
    }

    [Fact]
    public void DropPolicy_UnknownValue_Throws()
    {
        var act = () => JsonSerializer.Deserialize<DropPolicy>("\"DropMaybe\"", JsonOptions);
        act.Should().Throw<JsonException>();
    }

    // ========================================================================
    // DeliveryMode round-trip — and the ExactlyOnce locked invariant
    // ========================================================================

    [Theory]
    [InlineData("AtMostOnce", DeliveryMode.AtMostOnce)]
    [InlineData("AtLeastOnce", DeliveryMode.AtLeastOnce)]
    public void DeliveryMode_DeserializesFromString(string text, DeliveryMode expected)
    {
        var quoted = $"\"{text}\"";
        var actual = JsonSerializer.Deserialize<DeliveryMode>(quoted, JsonOptions);
        actual.Should().Be(expected);
    }

    [Fact]
    public void DeliveryMode_HasNoExactlyOnceMember()
    {
        // STRUCTURAL INVARIANT — pins blueprint §19.7.
        //
        // ExactlyOnce delivery is explicitly out of scope for v1. Adding it
        // requires blueprint revision and is treated as an architectural
        // change. This test fails the moment anyone introduces an
        // ExactlyOnce enum member, regardless of whether they wire it into
        // any code path.
        var names = Enum.GetNames<DeliveryMode>();

        names.Should().NotContain("ExactlyOnce");
        names.Should().BeEquivalentTo(new[] { "AtMostOnce", "AtLeastOnce" });
    }

    [Fact]
    public void DeliveryMode_DeserializeExactlyOnce_Throws()
    {
        // BEHAVIORAL INVARIANT — pins blueprint §19.7.
        //
        // Even if someone bypasses the structural test by reintroducing the
        // enum member with a different name, attempting to deserialize the
        // string "ExactlyOnce" must fail. Combined with
        // DeliveryMode_HasNoExactlyOnceMember above, this catches
        // reintroduction at both the structural and behavioral level.
        var act = () => JsonSerializer.Deserialize<DeliveryMode>("\"ExactlyOnce\"", JsonOptions);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void DeliveryMode_UnknownValue_Throws()
    {
        var act = () => JsonSerializer.Deserialize<DeliveryMode>("\"BestEffort\"", JsonOptions);
        act.Should().Throw<JsonException>();
    }

    // ========================================================================
    // Case-insensitive matching (industrial config files are written by hand)
    // ========================================================================

    [Theory]
    [InlineData("\"none\"")]
    [InlineData("\"NONE\"")]
    [InlineData("\"None\"")]
    [InlineData("\"nOnE\"")]
    public void BufferMode_CaseInsensitiveMatching(string quoted)
    {
        // PropertyNameCaseInsensitive on JsonSerializerOptions also makes
        // string-enum matching case-insensitive. Industrial configs are
        // hand-written and case errors are common; we accept them.
        var value = JsonSerializer.Deserialize<BufferMode>(quoted, JsonOptions);
        value.Should().Be(BufferMode.None);
    }
}
