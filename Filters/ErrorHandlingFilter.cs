using Cocona.Command.Binder;
using Cocona.Filters;
using NorthernRange.Errors;
using NorthernRange.Output;

namespace NorthernRange.Filters;

/// <summary>
/// Turns every failure inside a command into one stderr line and a documented
/// exit code. Applied per command method: Cocona does not propagate class-level
/// filters into <c>[HasSubCommands]</c> classes.
/// </summary>
public class ErrorHandlingFilter : CommandFilterAttribute
{
    public override async ValueTask<int> OnCommandExecutionAsync(
        CoconaCommandExecutingContext ctx, CommandExecutionDelegate next)
    {
        try
        {
            return await next(ctx);
        }
        catch (NrException ex)
        {
            ErrorOutput.Write(ex.ExitCode, ex.Message);
            return ex.ExitCode;
        }
        catch (ParameterBinderException ex)
        {
            // Missing required option, missing argument, or a value that could
            // not be converted. Cocona's own text leaks CLR type names.
            ErrorOutput.Write(ExitCodes.InvalidArguments, Describe(ex));
            return ExitCodes.InvalidArguments;
        }
        catch (OperationCanceledException)
        {
            ErrorOutput.Write(ExitCodes.Cancelled, "Cancelled.");
            return ExitCodes.Cancelled;
        }
        catch (Exception ex)
        {
            ErrorOutput.Write(ExitCodes.GeneralError, $"Unexpected error: {ex.Message}");
            return ExitCodes.GeneralError;
        }
    }

    public static string Describe(ParameterBinderException ex)
    {
        var name = ex.Option is not null ? "--" + ex.Option.Name
                 : ex.Argument is not null ? "<" + ex.Argument.Name + ">"
                 : "argument";
        var type = ex.Option?.UnwrappedOptionType ?? ex.Argument?.UnwrappedArgumentType;

        return ex.Result switch
        {
            ParameterBinderResult.InsufficientOption      => $"Missing required option {name}.",
            ParameterBinderResult.InsufficientOptionValue => $"Option {name} needs a value.",
            ParameterBinderResult.InsufficientArgument    => $"Missing required argument {name}.",
            _ => $"Invalid value for {name}: expected {Expected(type)}.",
        };
    }

    private static string Expected(Type? type) => type switch
    {
        null => "a value",
        _ when type == typeof(int) || type == typeof(long) => "an integer",
        _ when type == typeof(bool) => "true or false",
        _ when type.IsEnum => string.Join(", ", Enum.GetNames(type)).ToLowerInvariant(),
        _ => "text",
    };
}
