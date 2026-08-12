// ============================================================================
// Tests: ManagementOptions — pin the safety semantics. The
//        IsNetworkExposedWithoutAuth flag drives both the startup
//        WARNING and the Prometheus gauge; any drift here is a
//        security regression.
// ============================================================================

using ElpisEdgeConnect.Management.Options;
using FluentAssertions;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class ManagementOptionsTests
{
    [Fact]
    public void Defaults_AreLocalhostOnly_AndNoAuth()
    {
        var opts = new ManagementOptions();
        opts.BindAddress.Should().Be("127.0.0.1");
        opts.Port.Should().Be(5080);
        opts.Auth.Mode.Should().Be(ManagementAuthMode.None);
        opts.IsLoopbackBinding.Should().BeTrue();
        opts.IsNetworkExposedWithoutAuth.Should().BeFalse("default config is safe");
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("10.0.0.5", false)]
    [InlineData("192.168.1.100", false)]
    public void IsLoopbackBinding_RecognisesAllLoopbackForms(string bind, bool expectedLoopback)
    {
        var opts = new ManagementOptions { BindAddress = bind };
        opts.IsLoopbackBinding.Should().Be(expectedLoopback);
    }

    [Fact]
    public void IsNetworkExposedWithoutAuth_FlagsTheUnsafeCombo()
    {
        var unsafe1 = new ManagementOptions { BindAddress = "0.0.0.0" };
        unsafe1.IsNetworkExposedWithoutAuth.Should().BeTrue(
            "0.0.0.0 + no auth is the configuration that warrants a loud warning");

        var unsafe2 = new ManagementOptions { BindAddress = "10.0.0.5" };
        unsafe2.IsNetworkExposedWithoutAuth.Should().BeTrue();
    }

    [Fact]
    public void IsNetworkExposedWithoutAuth_FalseWhenAuthEnabled()
    {
        var withAuth = new ManagementOptions
        {
            BindAddress = "0.0.0.0",
            Auth = new ManagementAuthOptions
            {
                Mode = ManagementAuthMode.Basic,
                Users = new[]
                {
                    new ManagementUser { Username = "admin", Password = "x" },
                },
            },
        };
        withAuth.IsNetworkExposedWithoutAuth.Should().BeFalse(
            "Basic auth makes a network binding safe-by-default");
    }

    [Fact]
    public void BaseUrl_RendersAsHttp()
    {
        var opts = new ManagementOptions { BindAddress = "0.0.0.0", Port = 8080 };
        opts.BaseUrl.Should().Be("http://0.0.0.0:8080");
    }
}
