// ============================================================================
// Tests: Focas2BrowseStatusMapping — pins the result→HTTP-status contract
//        defined in M.2b.3 plan v3 §6.4 and ratified by the user in Step 6.
//        Pure-function helper so we can pin the mapping without standing
//        up a WebApplicationFactory; the endpoint itself is too thin to
//        warrant an integration test.
// ============================================================================

using ElpisEdgeConnect.Management.Api;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ElpisEdgeConnect.Management.Tests;

public class Focas2BrowseStatusMappingTests
{
    [Fact]
    public void Success_Returns200()
        => Focas2BrowseStatusMapping.StatusCodeFor(Result(success: true)).Should().Be(StatusCodes.Status200OK);

    [Theory]
    [InlineData("LICENSE.MODULE_DISABLED", StatusCodes.Status403Forbidden)]
    [InlineData("FOCAS2.BROWSE_IN_FLIGHT", StatusCodes.Status409Conflict)]
    [InlineData("FOCAS2.CONFIG_INVALID", StatusCodes.Status400BadRequest)]
    public void StructuredErrorCodes_MapToTheirHttpStatus(string errorCode, int expected)
        => Focas2BrowseStatusMapping.StatusCodeFor(Result(success: false, errorCode: errorCode))
            .Should().Be(expected);

    [Theory]
    [InlineData("FOCAS2.CONNECT_FAILED")]
    [InlineData("FOCAS2.BROWSE_TIMEOUT")]
    [InlineData("FOCAS2.NATIVE_LIBRARY_MISSING")]
    [InlineData("FOCAS2.SOME_FUTURE_CONTROLLER_ERROR")]
    public void ControllerSideErrors_Return200WithSuccessFalseInBody(string errorCode)
        => Focas2BrowseStatusMapping.StatusCodeFor(Result(success: false, errorCode: errorCode))
            .Should().Be(StatusCodes.Status200OK,
                "controller/connect errors should let the wizard render the structured error inline rather than hitting the fetch error path");

    private static Focas2BrowseResultDto Result(bool success, string? errorCode = null) =>
        new()
        {
            ProbeId = "abcd1234",
            Success = success,
            ErrorCode = errorCode,
            ElapsedMs = 0,
        };
}
