using Microsoft.Extensions.Logging.Abstractions;
using NorthernRange.Gmail;
using NorthernRange.Mime;
using NorthernRange.Tests.Fakes;
using Xunit;

namespace NorthernRange.Tests;

/// <summary>
/// Pagination contract for <c>messages list</c>, exercised against an in-memory
/// Gmail. No network, no auth.
/// </summary>
public class MessageServiceListTests
{
    private static MessageService NewService() =>
        new(new MimeParser(), NullLogger<MessageService>.Instance);

    private static string MessageGetJson(string id) =>
        """{"id":"__ID__","threadId":"t-__ID__","snippet":"snippet __ID__","labelIds":["INBOX"],"payload":{"headers":[{"name":"From","value":"a@x"},{"name":"Subject","value":"S __ID__"},{"name":"Date","value":"Tue, 16 Sep 2026 10:00:00 +0000"}]}}"""
            .Replace("__ID__", id);

    [Fact]
    public async Task ListAsync_PassesFilters_AndPageToken_ToGmail()
    {
        var gmail = new FakeGmail()
            .OnPath("/users/me/messages", """{"messages":[],"resultSizeEstimate":0}""");

        await NewService().ListAsync(gmail.Service, "Label_7", "is:unread", 50, "TOKEN123", "minimal");

        var list = Assert.Single(gmail.Requests);
        Assert.EndsWith("/users/me/messages", list.Path);
        Assert.Equal("Label_7", list["labelIds"]);
        Assert.Equal("is:unread", list["q"]);
        Assert.Equal("50", list["maxResults"]);
        Assert.Equal("TOKEN123", list["pageToken"]);
    }

    [Fact]
    public async Task ListAsync_FirstPage_SendsNoPageToken()
    {
        var gmail = new FakeGmail()
            .OnPath("/users/me/messages", """{"messages":[],"resultSizeEstimate":0}""");

        await NewService().ListAsync(gmail.Service, "INBOX", null, 25, null, "minimal");

        Assert.Null(gmail.Requests[0]["pageToken"]);
        Assert.Null(gmail.Requests[0]["q"]);
    }

    [Fact]
    public async Task ListAsync_ReturnsNextPageToken_FromGmail()
    {
        var gmail = new FakeGmail()
            .OnPath("/users/me/messages", """{"messages":[{"id":"m1","threadId":"t1"}],"nextPageToken":"NEXT-1","resultSizeEstimate":201}""");

        var result = await NewService().ListAsync(gmail.Service, "INBOX", null, 1, null, "minimal");

        Assert.Equal("NEXT-1", result.NextPageToken);
        Assert.Equal(201, result.ResultSizeEstimate);
        Assert.Single(result.Messages);
    }

    [Fact]
    public async Task ListAsync_EmptyPageWithToken_KeepsToken_AndFetchesNothing()
    {
        // Gmail does this when a label filter and a query are combined: a page
        // with no messages but a token pointing further along.
        var gmail = new FakeGmail()
            .OnPath("/users/me/messages", """{"nextPageToken":"NEXT-2","resultSizeEstimate":40}""");

        var result = await NewService().ListAsync(gmail.Service, "INBOX", "from:x", 25, null, "metadata");

        Assert.Empty(result.Messages);
        Assert.Equal("NEXT-2", result.NextPageToken);
        Assert.Single(gmail.Requests); // no per-message gets
    }

    [Fact]
    public async Task ListAsync_LastPage_HasNullToken()
    {
        var gmail = new FakeGmail()
            .OnPath("/users/me/messages", """{"messages":[{"id":"m9","threadId":"t9"}],"resultSizeEstimate":1}""");

        var result = await NewService().ListAsync(gmail.Service, "INBOX", null, 25, "LAST", "minimal");

        Assert.Null(result.NextPageToken);
    }

    [Fact]
    public async Task ListAsync_Minimal_DoesNotFetchPerMessage()
    {
        var gmail = new FakeGmail()
            .OnPath("/users/me/messages", """{"messages":[{"id":"m1","threadId":"t1","labelIds":["INBOX"]},{"id":"m2","threadId":"t2"}]}""");

        var result = await NewService().ListAsync(gmail.Service, "INBOX", null, 25, null, "minimal");

        Assert.Single(gmail.Requests);
        Assert.Equal(["m1", "m2"], result.Messages.Select(m => m.Id));
        Assert.Null(result.Messages[0].Subject);
    }

    [Fact]
    public async Task ListAsync_Metadata_FetchesEachMessage_AndPreservesOrder()
    {
        var gmail = new FakeGmail()
            .OnPath("/users/me/messages", """{"messages":[{"id":"m3"},{"id":"m1"},{"id":"m2"}],"nextPageToken":"N"}""")
            .On(r => r.RequestUri!.AbsolutePath.Contains("/users/me/messages/"),
                r => MessageGetJson(r.RequestUri!.AbsolutePath.Split('/').Last()));

        var result = await NewService().ListAsync(gmail.Service, "INBOX", null, 25, null, "metadata");

        Assert.Equal(["m3", "m1", "m2"], result.Messages.Select(m => m.Id));
        Assert.Equal("S m1", result.Messages[1].Subject);
        Assert.Equal(4, gmail.Requests.Count); // 1 list + 3 gets
        Assert.Equal("metadata", gmail.Requests[1]["format"]);
        Assert.Equal("N", result.NextPageToken);
    }
}
