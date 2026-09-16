using Microsoft.Extensions.Logging.Abstractions;
using NorthernRange.Gmail;
using NorthernRange.Mime;
using NorthernRange.Tests.Fakes;
using Xunit;

namespace NorthernRange.Tests;

public class ThreadServiceListTests
{
    private static ThreadService NewService() =>
        new(new MimeParser(), NullLogger<ThreadService>.Instance);

    [Fact]
    public async Task ListAsync_PassesFilters_AndPageToken()
    {
        var gmail = new FakeGmail()
            .OnPath("/users/me/threads", """{"threads":[{"id":"t1","snippet":"hi","historyId":"5"}],"nextPageToken":"T-NEXT","resultSizeEstimate":9}""");

        var result = await NewService().ListAsync(gmail.Service, "Label_2", "has:attachment", 10, "T-PREV");

        var req = Assert.Single(gmail.Requests);
        Assert.EndsWith("/users/me/threads", req.Path);
        Assert.Equal("Label_2", req["labelIds"]);
        Assert.Equal("has:attachment", req["q"]);
        Assert.Equal("10", req["maxResults"]);
        Assert.Equal("T-PREV", req["pageToken"]);

        Assert.Equal("T-NEXT", result.NextPageToken);
        Assert.Equal(9, result.ResultSizeEstimate);
        Assert.Equal("t1", Assert.Single(result.Threads).Id);
    }

    [Fact]
    public async Task ListAsync_EmptyPageWithToken_KeepsToken()
    {
        var gmail = new FakeGmail()
            .OnPath("/users/me/threads", """{"nextPageToken":"T-2"}""");

        var result = await NewService().ListAsync(gmail.Service, "INBOX", null, 25, null);

        Assert.Empty(result.Threads);
        Assert.Equal("T-2", result.NextPageToken);
    }
}
