using NorthernRange.Gmail;
using Xunit;

namespace NorthernRange.Tests;

// IsLikelyLabelId decides whether to try a direct ID lookup first (fast path)
// or resolve a display name via the Labels.List round-trip (slow path). The
// contract: Gmail system labels are all-caps; user label IDs have the form
// "Label_<digits>"; anything else is a display name. All-caps user labels
// (e.g. a label literally named "URGENT") are handled by the NotFound →
// re-resolve fallback in GetAsync/DeleteAsync, not by this predicate.
public class LabelServiceHeuristicTests
{
    [Theory]
    [InlineData("INBOX")]
    [InlineData("SENT")]
    [InlineData("DRAFT")]
    [InlineData("SPAM")]
    [InlineData("TRASH")]
    [InlineData("STARRED")]
    [InlineData("IMPORTANT")]
    [InlineData("UNREAD")]
    [InlineData("CATEGORY_PROMOTIONS")]
    [InlineData("CATEGORY_SOCIAL")]
    [InlineData("CATEGORY_UPDATES")]
    [InlineData("CATEGORY_FORUMS")]
    [InlineData("CATEGORY_PERSONAL")]
    public void SystemLabels_AreLikelyIds(string value)
    {
        Assert.True(LabelService.IsLikelyLabelId(value));
    }

    [Theory]
    [InlineData("Label_1")]
    [InlineData("Label_18")]
    [InlineData("Label_1234567890")]
    public void UserLabelIds_WithLabelPrefix_AreLikelyIds(string value)
    {
        Assert.True(LabelService.IsLikelyLabelId(value));
    }

    [Theory]
    [InlineData("Work")]
    [InlineData("Work/Projects")]
    [InlineData("Inbox")]       // mixed case → not a system label
    [InlineData("inbox")]       // lower case → not a system label
    [InlineData("Receipts 2026")]
    [InlineData("to-do")]
    [InlineData("Clients-A")]   // digits + letters + punctuation, not all-caps
    [InlineData("rotation A")]  // space + mixed case
    public void DisplayNames_AreNotLikelyIds(string value)
    {
        Assert.False(LabelService.IsLikelyLabelId(value));
    }

    [Theory]
    [InlineData("URGENT")]      // all-caps user label looks like a system ID
    [InlineData("TODO")]        // same risk
    public void AllCapsUserNames_AreTreatedAsIds_FallbackHandlesIt(string value)
    {
        // Documented behaviour: predicate returns true for all-caps user
        // names; GetAsync / DeleteAsync re-resolve via name lookup on the
        // resulting NotFound.
        Assert.True(LabelService.IsLikelyLabelId(value));
    }

    [Fact]
    public void EmptyString_IsNotLikelyId()
    {
        Assert.False(LabelService.IsLikelyLabelId(""));
    }

    [Theory]
    [InlineData("123")]         // all digits is all-caps-equivalent, caller gets NotFound
    [InlineData("Label_")]      // malformed Label_ prefix is accepted; API returns NotFound
    public void EdgeCases_PinnedBehaviour(string value)
    {
        // These are not the common path. Pin current behaviour so a future
        // tightening of the heuristic surfaces here first.
        Assert.True(LabelService.IsLikelyLabelId(value));
    }
}
