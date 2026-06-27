using System.Net;
using Google;
using NorthernRange.Errors;
using NorthernRange.Gmail;
using Xunit;

namespace NorthernRange.Tests;

public class GmailErrorMapperTests
{
    private static GoogleApiException Ex(HttpStatusCode code) =>
        new("gmail", "boom") { HttpStatusCode = code };

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ExitCodes.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized, ExitCodes.AuthRequired)]
    [InlineData(HttpStatusCode.Forbidden, ExitCodes.ApiError)]
    [InlineData(HttpStatusCode.BadRequest, ExitCodes.ApiError)]
    [InlineData(HttpStatusCode.InternalServerError, ExitCodes.ApiError)]
    public void Map_MapsStatusToExitCode(HttpStatusCode status, int expectedExit)
    {
        var nr = GmailErrorMapper.Map(Ex(status));
        Assert.Equal(expectedExit, nr.ExitCode);
    }

    [Fact]
    public void Map_UsesCustomNotFoundMessage_WhenProvided()
    {
        var nr = GmailErrorMapper.Map(Ex(HttpStatusCode.NotFound), "Message 'abc' not found.");
        Assert.Equal(ExitCodes.NotFound, nr.ExitCode);
        Assert.Equal("Message 'abc' not found.", nr.Message);
    }

    [Fact]
    public void Map_NotFoundWithoutCustomMessage_UsesGenericMessage()
    {
        var nr = GmailErrorMapper.Map(Ex(HttpStatusCode.NotFound));
        Assert.Contains("not found", nr.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map_CustomNotFoundMessage_IgnoredForNonNotFoundStatus()
    {
        // The custom message only applies to 404s.
        var nr = GmailErrorMapper.Map(Ex(HttpStatusCode.Unauthorized), "Message 'abc' not found.");
        Assert.DoesNotContain("abc", nr.Message);
        Assert.Equal(ExitCodes.AuthRequired, nr.ExitCode);
    }
}
