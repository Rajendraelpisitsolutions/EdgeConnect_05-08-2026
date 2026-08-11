// ============================================================================
// File: Errors/AdapterErrorTests.cs
// ============================================================================

using System;
using ElpisEdgeConnect.Core.Errors;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Core.Tests.Errors;

public sealed class AdapterErrorTests
{
    [Fact]
    public void AdapterException_WrapsErrorAndExposesMessage()
    {
        var err = new AdapterError
        {
            Code = "FOCAS2.HANDLE_EXHAUSTED",
            Category = ErrorCategory.ResourceExhausted,
            Message = "No free FOCAS2 handles available",
            Retryable = true,
            SuggestedBackoff = TimeSpan.FromSeconds(5),
        };

        var ex = new AdapterException(err);

        ex.Error.Should().BeSameAs(err);
        ex.Message.Should().Be("No free FOCAS2 handles available");
    }

    [Fact]
    public void ConfigurationFactory_ProducesNonRetryableConfigCategory()
    {
        var ex = AdapterException.Configuration("CORE.CONFIG_INVALID", "Missing IP");

        ex.Error.Category.Should().Be(ErrorCategory.Configuration);
        ex.Error.Retryable.Should().BeFalse();
        ex.Error.Code.Should().Be("CORE.CONFIG_INVALID");
    }

    [Fact]
    public void NetworkFactory_ProducesRetryableNetworkCategory()
    {
        var ex = AdapterException.Network("MODBUS.TIMEOUT", "Read timeout after 3s");

        ex.Error.Category.Should().Be(ErrorCategory.Network);
        ex.Error.Retryable.Should().BeTrue();
    }

    [Fact]
    public void NetworkFactory_WithInnerException_PreservesInner()
    {
        var inner = new System.Net.Sockets.SocketException();
        var ex = AdapterException.Network("MODBUS.CONN_RESET", "Connection reset", inner);

        ex.InnerException.Should().BeSameAs(inner);
        ex.Error.Retryable.Should().BeTrue();
    }

    [Fact]
    public void LicenseFactory_ProducesNonRetryableLicenseCategory()
    {
        var ex = AdapterException.License(CoreErrors.LicenseModuleDisabled, "Modbus module not licensed");

        ex.Error.Category.Should().Be(ErrorCategory.License);
        ex.Error.Retryable.Should().BeFalse();
    }

    [Fact]
    public void ConfigurationException_IsAdapterException()
    {
        var ex = new ConfigurationException(CoreErrors.ConfigMissingField, "Missing Connection.IpAddress");

        ex.Should().BeAssignableTo<AdapterException>();
        ex.Error.Category.Should().Be(ErrorCategory.Configuration);
    }

    [Fact]
    public void LicenseException_IsAdapterException()
    {
        var ex = new LicenseException(CoreErrors.LicenseExpired, "License expired on 2026-01-01");

        ex.Should().BeAssignableTo<AdapterException>();
        ex.Error.Category.Should().Be(ErrorCategory.License);
    }

    [Fact]
    public void RouteException_RetryableVariantCarriesFlag()
    {
        var ex = new RouteException(
            CoreErrors.RouteDeliveryFailed,
            "HTTP sink returned 503",
            ErrorCategory.Network,
            retryable: true);

        ex.Error.Retryable.Should().BeTrue();
        ex.Error.Category.Should().Be(ErrorCategory.Network);
    }

    [Fact]
    public void BufferException_PreservesInnerException()
    {
        var inner = new System.IO.IOException("disk full");
        var ex = new BufferException(CoreErrors.BufferIoError, "Failed to write buffer page", inner);

        ex.InnerException.Should().BeSameAs(inner);
        ex.Error.Code.Should().Be(CoreErrors.BufferIoError);
    }
}
