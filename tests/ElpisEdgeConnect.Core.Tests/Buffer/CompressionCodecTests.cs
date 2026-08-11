// ============================================================================
// File: Buffer/CompressionCodecTests.cs
// Covers: round-trip correctness + ratio expectation on synthetic CNC data.
// ============================================================================

using System;
using System.IO;
using ElpisEdgeConnect.Core.Buffer;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Buffer;

public sealed class CompressionCodecTests
{
    [Fact]
    public void RoundTrip_NonEmpty_BytesIdentical()
    {
        var data = new byte[1024];
        new Random(7).NextBytes(data);

        var compressed = CompressionCodec.Compress(data);
        var decompressed = CompressionCodec.Decompress(compressed);

        decompressed.Should().Equal(data);
    }

    [Fact]
    public void RoundTrip_Empty_ReturnsEmpty()
    {
        var compressed = CompressionCodec.Compress(Array.Empty<byte>());
        var decompressed = CompressionCodec.Decompress(compressed);
        decompressed.Should().BeEmpty();
    }

    [Fact]
    public void Compress_RealisticCncBatch_Achieves_AtLeast_5x_Ratio()
    {
        // Build the realistic CNC batch as a single large concatenated payload
        // (the C2b persistent buffer compresses batches, not individual rows).
        var batch = C2aTestFixtures.RealisticCncBatch(2000);
        using var ms = new MemoryStream();
        foreach (var p in batch)
        {
            var bytes = BinaryWriterFormat.Instance.Serialize(p);
            ms.Write(bytes, 0, bytes.Length);
        }
        var raw = ms.ToArray();
        var compressed = CompressionCodec.Compress(raw);

        var ratio = (double)raw.Length / compressed.Length;
        ratio.Should().BeGreaterThanOrEqualTo(5.0,
            $"realistic CNC payload should compress at least 5x; got {ratio:0.0}x ({raw.Length} → {compressed.Length})");
    }

    [Fact]
    public void Decompress_TruncatedHeader_Throws()
    {
        System.Action act = () => CompressionCodec.Decompress(new byte[2]);
        act.Should().Throw<System.InvalidOperationException>();
    }

    /// <summary>
    /// R4 pin (Mutation 19): a corrupted LZ4 body must be detected by the
    /// length-mismatch check inside <see cref="CompressionCodec.Decompress"/>.
    /// We use a length-prefix mutation (header still valid bytes, but the
    /// claimed original length differs) which exercises the same length-check
    /// guard from the inside out — catching a regression where the
    /// <c>written != originalLength</c> validation is dropped.
    /// </summary>
    [Fact]
    public void Decompress_LengthPrefixMismatch_Throws()
    {
        var data = new byte[512];
        new Random(11).NextBytes(data);
        var compressed = CompressionCodec.Compress(data);

        // Bump the length prefix by 1 so Decompress allocates a too-large
        // destination and the decoded byte count won't match.
        var mutated = (byte[])compressed.Clone();
        var bumpedLength = BitConverter.ToInt32(mutated, 0) + 1;
        BitConverter.GetBytes(bumpedLength).CopyTo(mutated, 0);

        System.Action act = () => CompressionCodec.Decompress(mutated);
        act.Should().Throw<System.InvalidOperationException>()
            .WithMessage("*decode produced*expected*");
    }

    /// <summary>R4 pin (companion): truncating the body must also fail.</summary>
    [Fact]
    public void Decompress_TruncatedBody_Throws()
    {
        var data = new byte[512];
        new Random(13).NextBytes(data);
        var compressed = CompressionCodec.Compress(data);

        var truncated = new byte[compressed.Length - 4];
        Array.Copy(compressed, truncated, truncated.Length);

        System.Action act = () => CompressionCodec.Decompress(truncated);
        act.Should().Throw<System.Exception>();
    }
}
