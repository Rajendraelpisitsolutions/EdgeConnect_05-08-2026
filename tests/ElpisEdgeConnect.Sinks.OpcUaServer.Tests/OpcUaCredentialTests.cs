// ============================================================================
// Tests: OpcUaCredential — the constant-time password compare and
//        the configured-vs-candidate matching used by the user-
//        identity validator.
// ============================================================================

using ElpisEdgeConnect.Sinks.OpcUaServer;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Sinks.OpcUaServer.Tests;

public class OpcUaCredentialTests
{
    [Fact]
    public void MatchesPassword_ReturnsTrue_OnExactMatch()
    {
        var cred = new OpcUaCredential { Username = "scada", Password = "s3cr3t!" };
        cred.MatchesPassword("s3cr3t!").Should().BeTrue();
    }

    [Fact]
    public void MatchesPassword_ReturnsFalse_OnLengthMismatch()
    {
        var cred = new OpcUaCredential { Username = "scada", Password = "s3cr3t!" };
        cred.MatchesPassword("s3cr3t").Should().BeFalse();
        cred.MatchesPassword("s3cr3t!!").Should().BeFalse();
    }

    [Fact]
    public void MatchesPassword_ReturnsFalse_OnContentMismatch()
    {
        var cred = new OpcUaCredential { Username = "scada", Password = "s3cr3t!" };
        cred.MatchesPassword("S3CR3T!").Should().BeFalse();
        cred.MatchesPassword("0000000").Should().BeFalse();
    }

    [Fact]
    public void MatchesPassword_ReturnsFalse_OnNull()
    {
        var cred = new OpcUaCredential { Username = "scada", Password = "s3cr3t!" };
        cred.MatchesPassword(null).Should().BeFalse();
    }

    [Fact]
    public void MatchesPassword_ReturnsTrue_OnEmptyMatch()
    {
        // Edge case — empty passwords aren't recommended, but the
        // compare should still be self-consistent.
        var cred = new OpcUaCredential { Username = "scada", Password = "" };
        cred.MatchesPassword("").Should().BeTrue();
        cred.MatchesPassword("x").Should().BeFalse();
    }
}
