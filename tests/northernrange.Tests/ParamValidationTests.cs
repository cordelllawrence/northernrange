using NorthernRange.Config;
using NorthernRange.Errors;
using NorthernRange.Filters;
using Xunit;

namespace NorthernRange.Tests;

public class ParamValidationTests
{
    [Theory]
    [InlineData("full", "full")]
    [InlineData("FULL", "full")]
    [InlineData("  Raw ", "raw")]
    public void RequireOneOf_IsCaseInsensitive_AndReturnsCanonical(string input, string expected)
    {
        var result = ParamValidation.RequireOneOf(input, ["full", "metadata", "raw"], "format");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void RequireOneOf_UnknownValue_ExitsInvalidArguments_ListingAllowed()
    {
        var ex = Assert.Throws<NrException>(() =>
            ParamValidation.RequireOneOf("bogus", ["full", "metadata"], "format"));
        Assert.Equal(ExitCodes.InvalidArguments, ex.ExitCode);
        Assert.Contains("--format", ex.Message);
        Assert.Contains("full, metadata", ex.Message);
        Assert.Contains("bogus", ex.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(250)]
    [InlineData(500)]
    public void RequireRange_InRange_DoesNotThrow(int value)
    {
        ParamValidation.RequireRange(value, 1, 500, "max");  // no exception
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    [InlineData(-5)]
    public void RequireRange_OutOfRange_ExitsInvalidArguments(int value)
    {
        var ex = Assert.Throws<NrException>(() => ParamValidation.RequireRange(value, 1, 500, "max"));
        Assert.Equal(ExitCodes.InvalidArguments, ex.ExitCode);
        Assert.Contains("max", ex.Message);
    }

    [Fact]
    public void SplitList_AcceptsCommaSeparated_AndRepeated_Combined()
    {
        var result = ParamValidation.SplitList(["From, Subject", "Date", "", " Cc "]);
        Assert.Equal(["From", "Subject", "Date", "Cc"], result);
    }

    [Fact]
    public void SplitList_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(ParamValidation.SplitList(null));
        Assert.Null(ParamValidation.SplitList([]));
        Assert.Null(ParamValidation.SplitList([",", " "]));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("YES", true)]
    [InlineData(" True ", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EnvVars_IsTruthy(string? value, bool expected)
    {
        Assert.Equal(expected, EnvVars.IsTruthy(value));
    }
}
