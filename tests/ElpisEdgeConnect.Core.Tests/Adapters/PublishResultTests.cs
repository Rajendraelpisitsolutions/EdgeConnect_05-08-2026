// ============================================================================
// File: Adapters/PublishResultTests.cs
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Adapters;
using ElpisEdgeConnect.Core.Errors;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Adapters;

public sealed class PublishResultTests
{
    [Fact]
    public void Successful_ReportsAllAccepted()
    {
        var result = PublishResult.Successful(count: 100, latency: TimeSpan.FromMilliseconds(45));

        result.Success.Should().BeTrue();
        result.AcceptedCount.Should().Be(100);
        result.RejectedCount.Should().Be(0);
        result.Error.Should().BeNull();
        result.Latency.Should().Be(TimeSpan.FromMilliseconds(45));
    }

    [Fact]
    public void Failed_CarriesError_AndZeroAcceptedAndZeroRejected()
    {
        var err = new AdapterError
        {
            Code = "MQTT.AUTH_REJECTED",
            Category = ErrorCategory.Authentication,
            Message = "Broker rejected credentials",
            Retryable = false,
        };

        var result = PublishResult.Failed(err, TimeSpan.FromMilliseconds(12));

        result.Success.Should().BeFalse();
        result.AcceptedCount.Should().Be(0);
        result.RejectedCount.Should().Be(0);
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("MQTT.AUTH_REJECTED");
        result.Error.Retryable.Should().BeFalse();
        result.Latency.Should().Be(TimeSpan.FromMilliseconds(12));
    }

    [Fact]
    public void Failed_WithNullError_Throws()
    {
        // Per the contract documented on PublishResult.Error, a failed result
        // must carry a non-null error. Passing null would silently produce an
        // internally inconsistent result and surface as a NullReferenceException
        // somewhere in the routing engine months later. The factory rejects it.
        var act = () => PublishResult.Failed(null!, TimeSpan.FromMilliseconds(12));

        act.Should().Throw<ArgumentNullException>().WithParameterName("error");
    }

    [Fact]
    public void PartialAcceptance_IsRepresentableDirectly()
    {
        // A sink that partially accepts a batch bookkeeps internally and
        // returns aggregate counts; per Section 19.4 the routing engine
        // treats Success as all-or-nothing for cursor advancement.
        var result = new PublishResult
        {
            Success = false,
            AcceptedCount = 80,
            RejectedCount = 20,
            Latency = TimeSpan.FromMilliseconds(50),
            Error = new AdapterError
            {
                Code = "HTTP.PARTIAL_REJECT",
                Category = ErrorCategory.Protocol,
                Message = "Some points rejected by endpoint",
                Retryable = true,
            },
        };

        result.Success.Should().BeFalse();
        result.AcceptedCount.Should().Be(80);
        result.RejectedCount.Should().Be(20);
    }
}
