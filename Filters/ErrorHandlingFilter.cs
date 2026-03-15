using Cocona.Filters;
using NorthernRange.Errors;

namespace NorthernRange.Filters;

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
            Console.Error.WriteLine(ex.Message);
            return ex.ExitCode;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");
            return ExitCodes.GeneralError;
        }
    }
}
