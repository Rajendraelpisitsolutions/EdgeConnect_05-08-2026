// ============================================================================
// Tests: SecretShapeDetector (ADR-0020 R-1, M-C Phase 1). Pins the deterministic
//        detectors (PEM / SSH / JWT) fire on real key material, do NOT fire on
//        ordinary config values (near-zero false positives), warn-only behaviour,
//        and determinism.
// ============================================================================

using System.Linq;
using System.Text.Json.Nodes;
using ElpisEdgeConnect.Management.Backup;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class SecretShapeDetectorTests
{
    private static System.Collections.Generic.IReadOnlyList<RedactionWarning> Scan(string json) =>
        SecretShapeDetector.Scan(JsonNode.Parse(json));

    [Theory]
    [InlineData("-----BEGIN PRIVATE KEY-----\\nMIIE...\\n-----END PRIVATE KEY-----", "PEM")]
    [InlineData("-----BEGIN CERTIFICATE-----abc", "PEM")]
    [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----xyz", "PEM")]
    [InlineData("ssh-rsa AAAAB3NzaC1yc2EAAAADAQAB user@host", "SSH")]
    [InlineData("ssh-ed25519 AAAAC3NzaC1lZDI1 user@host", "SSH")]
    [InlineData("ecdsa-sha2-nistp256 AAAAE2Vj", "SSH")]
    [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NSJ9.SflKxwRJSMeKKF2QT4", "JWT")]
    public void Scan_DetectsKeyMaterial(string secretValue, string _)
    {
        var warnings = Scan($$"""{ "someBenignKey": "{{secretValue}}" }""");

        warnings.Should().ContainSingle()
            .Which.Path.Should().Be("someBenignKey");
    }

    [Theory]
    [InlineData("opc.tcp://plc.factory.local:4840")]   // endpoint URL
    [InlineData("192.168.1.50")]                         // IP
    [InlineData("a3f1c2e9-1234-5678-9abc-def012345678")] // GUID
    [InlineData("1.2.3")]                                 // version string
    [InlineData("edgeconnect/{gatewayId}/data")]         // topic template
    [InlineData("HoldingRegister")]                       // enum-ish value
    [InlineData("Elpis EdgeConnect OPC UA Server")]       // app name
    public void Scan_DoesNotFlagOrdinaryValues(string value)
    {
        var warnings = Scan($$"""{ "field": "{{value}}" }""");
        warnings.Should().BeEmpty();
    }

    [Fact]
    public void Scan_RecordsNestedPath()
    {
        const string json = """
            { "sinks": [ { "connection": { "vendorToken": "-----BEGIN PRIVATE KEY-----zzz" } } ] }
            """;

        var warnings = Scan(json);

        warnings.Should().ContainSingle()
            .Which.Path.Should().Be("sinks[0].connection.vendorToken");
    }

    [Fact]
    public void Scan_IgnoresMaskMarkerAndMetadata()
    {
        // The detector runs AFTER redaction; masked values are "***" and the
        // sibling metadata is bools/numbers — none of which is key-shaped.
        const string json = """
            { "password": "***", "password__redacted": { "masked": true, "originalByteLength": 14 } }
            """;

        Scan(json).Should().BeEmpty();
    }

    [Fact]
    public void Scan_IsDeterministic()
    {
        const string json = """
            { "a": "ssh-rsa AAAAB3 x", "b": { "c": "-----BEGIN CERTIFICATE-----q" } }
            """;

        var first = Scan(json).Select(w => w.Path).ToList();
        var second = Scan(json).Select(w => w.Path).ToList();

        second.Should().Equal(first);
        first.Should().Equal(new[] { "a", "b.c" });
    }

    [Fact]
    public void Scan_Null_ReturnsEmpty()
    {
        SecretShapeDetector.Scan(null).Should().BeEmpty();
    }

    // ---- Phase 2: entropy / token-likelihood ----

    [Theory]
    [InlineData("AKIA1234567890ABCDEF0987654321ZY")]          // AWS-key-shaped (mixed, 32)
    [InlineData("sk-9aF3kQ2mZ8xV1nB7cR4tY6uH0jW5pL2")]         // API-key-shaped
    [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")] // 64-hex
    [InlineData("ghp_16C7e42F292c6912E7710c838347Ae178B4a")]   // token-shaped
    public void Scan_FlagsHighEntropyTokens(string token)
    {
        Scan($$"""{ "vendorField": "{{token}}" }""")
            .Should().ContainSingle().Which.Path.Should().Be("vendorField");
    }

    [Theory]
    [InlineData("a3f1c2e9-1234-5678-9abc-def012345678")]   // GUID
    [InlineData("01ARZ3NDEKTSV4RRFFQ69G5FAV")]              // ULID (26 Crockford base32)
    [InlineData("someVeryLongConfigurationFieldNameHere")] // long identifier, no digit
    [InlineData("supercalifragilisticexpialidocious")]     // long word, no digit
    [InlineData("https://agent.factory.local:5000/current")] // URL
    [InlineData("C:/ProgramData/EdgeConnect/certs/app.der")] // path
    [InlineData("opc.tcp://0.0.0.0:4840/edgeconnect")]      // endpoint
    [InlineData("urn:elpis:edgeconnect:opcua-client")]      // URN
    [InlineData("ns=2;s=GW/SRC/tag-stable-id-0001")]        // NodeId template
    [InlineData("2026.05.31.1430")]                          // dotted version
    public void Scan_DoesNotFlagBenignHighLookingValues(string value)
    {
        Scan($$"""{ "field": "{{value}}" }""").Should().BeEmpty();
    }

    [Fact]
    public void Scan_ShortRandomString_NotFlagged()
    {
        // Below the length floor — too short to confidently call a token.
        Scan("""{ "f": "aB3xK9zP2" }""").Should().BeEmpty();
    }
}
