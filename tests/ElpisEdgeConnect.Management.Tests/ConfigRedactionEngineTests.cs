// ============================================================================
// Tests: ConfigRedactionEngine — tier-aware JSON walker that masks or strips
//        secret-named properties and records each redacted path. The contract
//        these tests pin is the security boundary: a passing suite is what
//        guarantees backup/bundle exports don't leak credentials. Also pins
//        the locked determinism invariant (ADR-0020 plan v2 §1c) and the
//        sibling MASK-metadata shape (Q-5).
// ============================================================================

using System.Text.Json;
using ElpisEdgeConnect.Core.Configuration;
using ElpisEdgeConnect.Management.Backup;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class ConfigRedactionEngineTests
{
    private readonly ConfigRedactionEngine _engine = new();

    [Fact]
    public void Redact_TopLevelPassword_MaskedWithMarkerAndSibling()
    {
        const string input = """
            {
              "host": "127.0.0.1",
              "password": "supersecret"
            }
            """;

        var result = _engine.Redact(input);

        result.Paths.Should().ContainSingle().Which.Should().Be("password");
        result.RedactedJson.Should().Contain("\"***\"");
        result.RedactedJson.Should().NotContain("supersecret");
        result.RedactedJson.Should().Contain("\"host\": \"127.0.0.1\"",
            "non-secret values pass through unchanged");

        // Sibling metadata (Q-5 locked shape): masked flag + original byte length.
        using var doc = JsonDocument.Parse(result.RedactedJson);
        var meta = doc.RootElement.GetProperty("password__redacted");
        meta.GetProperty("masked").GetBoolean().Should().BeTrue();
        meta.GetProperty("originalByteLength").GetInt32().Should().Be(11);
    }

    [Fact]
    public void Redact_PrivateKey_StrippedEntirely_KeyAbsent()
    {
        const string input = """
            {
              "host": "127.0.0.1",
              "privateKey": "-----BEGIN PRIVATE KEY-----abc"
            }
            """;

        var result = _engine.Redact(input);

        result.Paths.Should().ContainSingle().Which.Should().Be("privateKey");
        result.RedactedJson.Should().NotContain("BEGIN PRIVATE KEY");

        using var doc = JsonDocument.Parse(result.RedactedJson);
        doc.RootElement.TryGetProperty("privateKey", out _).Should().BeFalse(
            "STRIP removes the key entirely — not even the name survives");
        doc.RootElement.TryGetProperty("privateKey__redacted", out _).Should().BeFalse(
            "STRIP has no sibling metadata; only MASK does");
    }

    [Fact]
    public void Redact_NestedMaskAndStrip_PathsRecorded()
    {
        const string input = """
            {
              "sources": [
                { "connection": { "host": "127.0.0.1", "password": "p1" } }
              ],
              "sinks": [
                { "connection": { "apiKey": "k1" } },
                { "connection": { "privateKey": "PEM..." } }
              ]
            }
            """;

        var result = _engine.Redact(input);

        result.Paths.Should().BeEquivalentTo(new[]
        {
            "sources[0].connection.password",
            "sinks[0].connection.apiKey",
            "sinks[1].connection.privateKey",
        });
        result.RedactedJson.Should().NotContain("p1").And.NotContain("k1").And.NotContain("PEM...");
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("PASSWORD")]
    [InlineData("PassWord")]
    public void Redact_CaseInsensitiveMatch(string propertyName)
    {
        var input = $$"""
            {
              "{{propertyName}}": "leaked"
            }
            """;

        var result = _engine.Redact(input);

        result.Paths.Should().ContainSingle();
        result.RedactedJson.Should().NotContain("leaked");
    }

    [Fact]
    public void Redact_NonSecretFields_PreservedUnchanged()
    {
        const string input = """
            {
              "instanceId": "modbus-1",
              "host": "127.0.0.1",
              "port": 5020,
              "tagDefinitions": [
                { "name": "spindle_rpm", "address": 0 }
              ]
            }
            """;

        var result = _engine.Redact(input);

        result.Paths.Should().BeEmpty();
        using var redoc = JsonDocument.Parse(result.RedactedJson);
        redoc.RootElement.GetProperty("instanceId").GetString().Should().Be("modbus-1");
        redoc.RootElement.GetProperty("port").GetInt32().Should().Be(5020);
        redoc.RootElement.GetProperty("tagDefinitions").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void Redact_EmptyObject_NoChanges()
    {
        var result = _engine.Redact("{}");
        result.Paths.Should().BeEmpty();
    }

    [Fact]
    public void Redact_MalformedJson_Throws()
    {
        var act = () => _engine.Redact("{ this is not json");
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Redact_EveryBaselineName_IsMaskedOrStripped_AndValueNeverLeaks()
    {
        // Defensive: every name in the baseline triggers redaction at its
        // declared tier. Catches an accidental removal or a tier downgrade.
        foreach (var (name, tier) in BackupSecretPatterns.Tiers)
        {
            var input = $$"""{ "{{name}}": "value-to-redact" }""";
            var result = _engine.Redact(input);

            result.Paths.Should().ContainSingle(because: $"property '{name}' must trigger redaction")
                                 .Which.Should().Be(name);
            result.RedactedJson.Should().NotContain("value-to-redact",
                because: $"value under '{name}' must not leak");

            using var doc = JsonDocument.Parse(result.RedactedJson);
            if (tier == BundleTier.Strip)
            {
                doc.RootElement.TryGetProperty(name, out _).Should().BeFalse(
                    $"'{name}' is STRIP — key must be absent");
            }
            else
            {
                tier.Should().Be(BundleTier.Mask, $"baseline names are only ever Mask or Strip; '{name}'");
                doc.RootElement.GetProperty(name).GetString().Should().Be("***",
                    $"'{name}' is MASK — value is the marker");
                doc.RootElement.TryGetProperty(name + "__redacted", out _).Should().BeTrue(
                    $"'{name}' is MASK — sibling metadata present");
            }
        }
    }

    [Fact]
    public void Redact_SecretValuedAsObject_StrippedAtomically_NotRecursed()
    {
        // A config might store a structured object under a secret-named key.
        // 'certificate' is STRIP: the whole subtree is removed, not recursed.
        const string input = """
            {
              "certificate": {
                "pem": "-----BEGIN-----",
                "fingerprint": "abc"
              }
            }
            """;

        var result = _engine.Redact(input);

        result.Paths.Should().ContainSingle().Which.Should().Be("certificate");
        result.RedactedJson.Should().NotContain("BEGIN").And.NotContain("fingerprint");
    }

    [Fact]
    public void Redact_IsDeterministic_IdenticalInputProducesIdenticalOutput()
    {
        // LOCKED invariant (ADR-0020 plan v2 §1c): same input + same rules ->
        // byte-for-byte identical redacted output AND identical provenance.
        const string input = """
            {
              "gateway": { "gatewayId": "gw-1" },
              "sources": [
                { "connection": { "host": "h", "password": "p", "privateKey": "k" } }
              ],
              "sinks": [
                { "connection": { "apiKey": "a", "token": "t", "secret": "s" } }
              ]
            }
            """;

        var first = _engine.Redact(input);
        var second = _engine.Redact(input);

        second.RedactedJson.Should().Be(first.RedactedJson,
            "redacted output must be byte-for-byte stable across runs");
        second.Paths.Should().Equal(first.Paths,
            "provenance order and content must be stable across runs");
    }

    [Fact]
    public void Redact_SplitsMaskedAndStrippedPaths()
    {
        // password -> Mask, privateKey -> Strip, host -> Include (baseline).
        const string input = """{ "password": "p", "privateKey": "k", "host": "h" }""";

        var result = _engine.Redact(input);

        result.MaskedPaths.Should().Equal(new[] { "password" });
        result.StrippedPaths.Should().Equal(new[] { "privateKey" });
        result.Paths.Should().BeEquivalentTo(new[] { "password", "privateKey" },
            "Paths is the union of masked and stripped");
    }

    [Fact]
    public void Redact_MaskByteLength_CountsUtf8Bytes()
    {
        // Multi-byte UTF-8: 'é' is 2 bytes. "pé" -> 1 + 2 = 3 bytes.
        const string input = """{ "password": "pé" }""";

        var result = _engine.Redact(input);

        using var doc = JsonDocument.Parse(result.RedactedJson);
        doc.RootElement.GetProperty("password__redacted")
            .GetProperty("originalByteLength").GetInt32().Should().Be(3);
    }
}
