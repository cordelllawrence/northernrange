using System.Reflection;
using Cocona;
using NorthernRange.Commands;
using Xunit;

namespace NorthernRange.Tests;

// ArgPrescan.HoistGlobalOptions reads GlobalOptions via
// `typeof(GlobalOptions).GetConstructors()[0]`. If a future change switches
// the record's declared constructor order, the pre-host flag hoisting would
// silently stop recognising global options. These tests pin the two
// assumptions: there is exactly one public constructor, and every parameter
// carries an [Option] attribute whose spelling matches a FlagNames constant.
public class GlobalOptionsReflectionTests
{
    [Fact]
    public void GlobalOptions_HasExactlyOnePublicConstructor()
    {
        var ctors = typeof(GlobalOptions).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Single(ctors);
    }

    [Fact]
    public void EveryGlobalOptionParameterCarriesAnOptionAttribute()
    {
        foreach (var p in typeof(GlobalOptions).GetConstructors()[0].GetParameters())
        {
            var opt = p.GetCustomAttribute<OptionAttribute>();
            Assert.NotNull(opt);
        }
    }

    [Theory]
    [InlineData(FlagNames.Json)]
    [InlineData(FlagNames.Ui)]
    [InlineData(FlagNames.Credentials)]
    [InlineData(FlagNames.Config)]
    [InlineData(FlagNames.Account)]
    [InlineData(FlagNames.Log)]
    [InlineData(FlagNames.LogFormat)]
    [InlineData(FlagNames.LogFile)]
    [InlineData(FlagNames.LogLevel)]
    public void EachKnownLongNameIsBoundToAGlobalOption(string expectedName)
    {
        var names = typeof(GlobalOptions).GetConstructors()[0].GetParameters()
            .Select(p => p.GetCustomAttribute<OptionAttribute>()?.Name)
            .ToHashSet();
        Assert.Contains(expectedName, names);
    }
}
