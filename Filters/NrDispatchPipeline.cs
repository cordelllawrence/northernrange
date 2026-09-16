using Cocona.Command.Dispatcher;
using Microsoft.Extensions.DependencyInjection;
using NorthernRange.Errors;
using NorthernRange.Output;

namespace NorthernRange.Filters;

/// <summary>
/// Wraps Cocona's dispatch pipeline so the outermost step is ours. Two jobs:
/// reject unknown options with exit 2 (Cocona's own middleware uses 129 and
/// runs before any command filter), and record that a command was dispatched
/// so <c>Program</c> can tell an unknown-command failure (also 2) apart from a
/// command that returned 1.
/// </summary>
public sealed class NrDispatchPipeline : ICoconaCommandDispatcherPipelineBuilder
{
    private readonly ICoconaCommandDispatcherPipelineBuilder _inner;

    /// <summary>True once a resolved command entered the pipeline.</summary>
    public static bool Dispatched { get; private set; }

    public NrDispatchPipeline(ICoconaCommandDispatcherPipelineBuilder inner) => _inner = inner;

    public ICoconaCommandDispatcherPipelineBuilder UseMiddleware<T>() where T : CommandDispatcherMiddleware
    { _inner.UseMiddleware<T>(); return this; }

    public ICoconaCommandDispatcherPipelineBuilder UseMiddleware(Func<CommandDispatchDelegate, CommandDispatchContext, ValueTask<int>> middleware)
    { _inner.UseMiddleware(middleware); return this; }

    public ICoconaCommandDispatcherPipelineBuilder UseMiddleware(Func<CommandDispatchDelegate, IServiceProvider, CommandDispatcherMiddleware> factory)
    { _inner.UseMiddleware(factory); return this; }

    public CommandDispatchDelegate Build()
    {
        var inner = _inner.Build();
        return ctx =>
        {
            Dispatched = true;

            var unknown = ctx.ParsedCommandLine.UnknownOptions;
            if (unknown.Count > 0)
            {
                var list = string.Join(", ", unknown.Select(o => o.Length == 1 ? "-" + o : "--" + o));
                ErrorOutput.Write(ExitCodes.InvalidArguments,
                    $"Unknown option {list}. Run 'nr <command> <subcommand> --help' to see the accepted options.");
                return new ValueTask<int>(ExitCodes.InvalidArguments);
            }

            return inner(ctx);
        };
    }

    /// <summary>Replaces Cocona's pipeline builder registration with this wrapper.</summary>
    public static void Register(IServiceCollection services)
    {
        var existing = services.LastOrDefault(d => d.ServiceType == typeof(ICoconaCommandDispatcherPipelineBuilder));
        if (existing is not null)
            services.Remove(existing);

        services.AddSingleton<ICoconaCommandDispatcherPipelineBuilder>(sp =>
        {
            var inner = existing switch
            {
                { ImplementationInstance: ICoconaCommandDispatcherPipelineBuilder i } => i,
                { ImplementationFactory: { } f } => (ICoconaCommandDispatcherPipelineBuilder)f(sp),
                { ImplementationType: { } t } => (ICoconaCommandDispatcherPipelineBuilder)ActivatorUtilities.CreateInstance(sp, t),
                _ => ActivatorUtilities.CreateInstance<CoconaCommandDispatcherPipelineBuilder>(sp),
            };
            return new NrDispatchPipeline(inner);
        });
    }
}
