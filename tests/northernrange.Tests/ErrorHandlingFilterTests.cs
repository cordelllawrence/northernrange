using Cocona.Command;
using Cocona.Command.Binder;
using Cocona.Filters;
using NorthernRange.Errors;
using NorthernRange.Filters;
using NorthernRange.Output;
using Xunit;

namespace NorthernRange.Tests;

/// <summary>
/// The filter is the last line of the exit-code contract: every exception a
/// command throws becomes one stderr line and a documented code.
/// </summary>
[Collection("console")]
public class ErrorHandlingFilterTests
{
    private static async Task<(int Code, string Stderr)> Run(Func<ValueTask<int>> body, bool json = false)
    {
        var original = Console.Error;
        var sw = new StringWriter();
        Console.SetError(sw);
        var wasJson = ErrorOutput.JsonMode;
        ErrorOutput.JsonMode = json;
        try
        {
            var code = await new ErrorHandlingFilter().OnCommandExecutionAsync(null!, _ => body());
            return (code, sw.ToString().TrimEnd());
        }
        finally
        {
            ErrorOutput.JsonMode = wasJson;
            Console.SetError(original);
        }
    }

    [Fact]
    public async Task Success_PassesCodeThrough()
    {
        var (code, err) = await Run(() => new ValueTask<int>(0));
        Assert.Equal(0, code);
        Assert.Equal("", err);
    }

    [Theory]
    [InlineData(ExitCodes.InvalidArguments)]
    [InlineData(ExitCodes.AuthRequired)]
    [InlineData(ExitCodes.ApiError)]
    [InlineData(ExitCodes.NotFound)]
    [InlineData(ExitCodes.FileError)]
    public async Task NrException_ExitsWithItsCode_AndMessageOnly(int expected)
    {
        var (code, err) = await Run(() => throw new NrException(expected, "boom"));
        Assert.Equal(expected, code);
        Assert.Equal("boom", err);
    }

    [Fact]
    public async Task UnexpectedException_ExitsOne()
    {
        var (code, err) = await Run(() => throw new InvalidOperationException("kaboom"));
        Assert.Equal(ExitCodes.GeneralError, code);
        Assert.Equal("Unexpected error: kaboom", err);
    }

    [Fact]
    public async Task Cancellation_Exits130()
    {
        var (code, err) = await Run(() => throw new OperationCanceledException());
        Assert.Equal(ExitCodes.Cancelled, code);
        Assert.Equal("Cancelled.", err);
    }

    [Fact]
    public async Task BinderException_ExitsTwo_WithCleanMessage()
    {
        var option = new CommandOptionDescriptor(typeof(int?), "max", [], "", CoconaDefaultValue.None, null, CommandOptionFlags.None, []);
        var ex = new ParameterBinderException(ParameterBinderResult.TypeNotSupported, option, null, null);

        var (code, err) = await Run(() => throw ex);

        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Equal("Invalid value for --max: expected an integer.", err);
        Assert.DoesNotContain("Nullable", err);
    }

    [Fact]
    public void Describe_MissingRequiredOption()
    {
        var option = new CommandOptionDescriptor(typeof(List<string>), "to", ['t'], "", CoconaDefaultValue.None, null, CommandOptionFlags.None, []);
        var ex = new ParameterBinderException(ParameterBinderResult.InsufficientOption, option, null, null);
        Assert.Equal("Missing required option --to.", ErrorHandlingFilter.Describe(ex));
    }

    [Fact]
    public void Describe_MissingRequiredArgument()
    {
        var arg = new CommandArgumentDescriptor(typeof(string), "id", 0, "", CoconaDefaultValue.None, []);
        var ex = new ParameterBinderException(ParameterBinderResult.InsufficientArgument, null, arg, null);
        Assert.Equal("Missing required argument <id>.", ErrorHandlingFilter.Describe(ex));
    }

    [Fact]
    public async Task JsonMode_WritesEnvelope_ToStderr()
    {
        var (code, err) = await Run(() => throw new NrException(ExitCodes.NotFound, "Label 'x' not found."), json: true);
        Assert.Equal(ExitCodes.NotFound, code);
        Assert.Equal("{\"error\":{\"code\":5,\"message\":\"Label 'x' not found.\"}}", err);
    }
}
