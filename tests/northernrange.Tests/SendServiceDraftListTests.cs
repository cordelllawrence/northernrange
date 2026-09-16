using Microsoft.Extensions.Logging.Abstractions;
using NorthernRange.Gmail;
using NorthernRange.Tests.Fakes;
using Xunit;

namespace NorthernRange.Tests;

public class SendServiceDraftListTests
{
    private static SendService NewService() => new(NullLogger<SendService>.Instance);

    private static string DraftGetJson(string id, string date) =>
        """{"id":"__ID__","message":{"id":"m-__ID__","snippet":"s","payload":{"headers":[{"name":"Subject","value":"D __ID__"},{"name":"To","value":"a@x"},{"name":"Date","value":"__DATE__"}]}}}"""
            .Replace("__ID__", id).Replace("__DATE__", date);

    [Fact]
    public async Task ListDraftsAsync_PassesPageToken_AndReturnsNext()
    {
        var gmail = new FakeGmail()
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/users/me/drafts"),
                _ => """{"drafts":[{"id":"d1"}],"nextPageToken":"D-NEXT","resultSizeEstimate":3}""")
            .On(r => r.RequestUri!.AbsolutePath.Contains("/users/me/drafts/"),
                r => DraftGetJson(r.RequestUri!.AbsolutePath.Split('/').Last(), "Tue, 16 Sep 2026 10:00:00 +0000"));

        var result = await NewService().ListDraftsAsync(gmail.Service, 7, "D-PREV");

        Assert.Equal("7", gmail.Requests[0]["maxResults"]);
        Assert.Equal("D-PREV", gmail.Requests[0]["pageToken"]);
        Assert.Equal("D-NEXT", result.NextPageToken);
        Assert.Equal("d1", Assert.Single(result.Drafts).DraftId);
    }

    [Fact]
    public async Task ListDraftsAsync_KeepsGmailOrder_AcrossDates()
    {
        // Older draft listed first by Gmail must stay first: a per-page re-sort
        // would interleave with the next page's order.
        var gmail = new FakeGmail()
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/users/me/drafts"),
                _ => """{"drafts":[{"id":"old"},{"id":"new"}]}""")
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/drafts/old"),
                _ => DraftGetJson("old", "Mon, 01 Sep 2026 10:00:00 +0000"))
            .On(r => r.RequestUri!.AbsolutePath.EndsWith("/drafts/new"),
                _ => DraftGetJson("new", "Tue, 16 Sep 2026 10:00:00 +0000"));

        var result = await NewService().ListDraftsAsync(gmail.Service, 25, null);

        Assert.Equal(["old", "new"], result.Drafts.Select(d => d.DraftId));
    }

    [Fact]
    public async Task ListDraftsAsync_EmptyPageWithToken_KeepsToken()
    {
        var gmail = new FakeGmail()
            .OnPath("/users/me/drafts", """{"nextPageToken":"D-2"}""");

        var result = await NewService().ListDraftsAsync(gmail.Service, 25, null);

        Assert.Empty(result.Drafts);
        Assert.Equal("D-2", result.NextPageToken);
    }
}
