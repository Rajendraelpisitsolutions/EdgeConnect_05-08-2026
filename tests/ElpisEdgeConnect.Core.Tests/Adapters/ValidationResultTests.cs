// ============================================================================
// File: Adapters/ValidationResultTests.cs
// ============================================================================

using ElpisEdgeConnect.Core.Adapters;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Adapters;

public sealed class ValidationResultTests
{
    [Fact]
    public void Success_HasNoErrorsOrWarnings()
    {
        var result = ValidationResult.Success();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Failure_ProducesOneError_AndIsInvalid()
    {
        var result = ValidationResult.Failure("CORE.CONFIG_INVALID", "Missing IP address", "Connection.IpAddress");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Code.Should().Be("CORE.CONFIG_INVALID");
        result.Errors[0].Message.Should().Be("Missing IP address");
        result.Errors[0].Path.Should().Be("Connection.IpAddress");
    }

    [Fact]
    public void SuccessWithWarnings_IsValidButCarriesWarnings()
    {
        var warning = new ValidationIssue
        {
            Code = "CORE.CONFIG_DEFAULT_USED",
            Message = "PollIntervalMs not specified; defaulting to 1000",
        };

        var result = ValidationResult.SuccessWithWarnings(warning);

        result.IsValid.Should().BeTrue();
        result.Warnings.Should().HaveCount(1);
        result.Warnings[0].Code.Should().Be("CORE.CONFIG_DEFAULT_USED");
    }
}
