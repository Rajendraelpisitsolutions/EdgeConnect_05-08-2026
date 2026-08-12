// ============================================================================
// File: ProtoProvenanceTests.cs
// Purpose: Guards the pinned Sparkplug B schema AND the vendored generated code
//          (ADR-0035 Rule 2): both must remain byte-identical to the recorded
//          pins. A failure here means a file was edited, replaced, or rewritten
//          by a checkout transformation without going through the re-pinning
//          procedure in docs/compliance/sparkplug-b-proto-provenance.md.
//          Integrity chain: pinned proto hash -> pinned toolchain ->
//          deterministic generation (regenerate.ps1 -Verify) -> pinned
//          generated-file hash (asserted here).
// ============================================================================

using System.Security.Cryptography;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.SparkplugB.Tests;

public sealed class ProtoProvenanceTests
{
    /// <summary>Pinned SHA-256 of sparkplug_b.proto at Tahu commit 46f25e79f34234e6145d11108660dfd9133ae50d.</summary>
    private const string PinnedProtoSha256 = "4432C5C483B7FB9732D0594C98A2E97DCA5E517E39C5374A8B918D837F0B4A19";

    /// <summary>Pinned byte length of the schema (diagnostic aid: a size change points at newline/checkout rewriting).</summary>
    private const long PinnedProtoLength = 8330;

    /// <summary>Pinned SHA-256 of the vendored SparkplugB.g.cs (libprotoc 35.1, csharp_opt=internal_access,file_extension=.g.cs).</summary>
    private const string PinnedGeneratedSha256 = "84E844E7CB5E6B369E49E071CD45F0AE961EA922BAEB95302F27449E3F5529C7";

    /// <summary>Pinned byte length of the vendored generated file.</summary>
    private const long PinnedGeneratedLength = 296399;

    [Fact]
    public void VendoredProto_Sha256AndLength_MatchPinnedProvenanceRecord()
    {
        var bytes = ReadArtifact("Protos", "sparkplug_b.proto");

        bytes.LongLength.Should().Be(PinnedProtoLength,
            "a length change indicates newline or checkout transformation of the pinned schema");
        Convert.ToHexString(SHA256.HashData(bytes)).Should().Be(PinnedProtoSha256,
            "the vendored sparkplug_b.proto is pinned and must never be edited; " +
            "see docs/compliance/sparkplug-b-proto-provenance.md for the re-pinning procedure");
    }

    [Fact]
    public void VendoredGeneratedCode_Sha256AndLength_MatchPinnedProvenanceRecord()
    {
        var bytes = ReadArtifact("Protobuf", "SparkplugB.g.cs");

        bytes.LongLength.Should().Be(PinnedGeneratedLength,
            "a length change indicates the vendored generated file was edited or rewritten");
        Convert.ToHexString(SHA256.HashData(bytes)).Should().Be(PinnedGeneratedSha256,
            "the vendored generated code must be byte-identical to deterministic regeneration " +
            "from the pinned schema (tools/sparkplug-proto/regenerate.ps1 -Verify); never hand-edit it");
    }

    private static byte[] ReadArtifact(string folder, string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, folder, fileName);
        File.Exists(path).Should().BeTrue($"the pinned artifact must be copied to test output (missing: {path})");
        return File.ReadAllBytes(path);
    }
}
