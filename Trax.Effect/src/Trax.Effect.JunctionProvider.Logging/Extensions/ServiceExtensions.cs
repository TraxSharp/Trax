using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerFactory;
using Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider;
using TraxEffectBuilder = Trax.Effect.Configuration.TraxEffectBuilder.TraxEffectBuilder;

namespace Trax.Effect.JunctionProvider.Logging.Extensions;

public static class ServiceExtensions
{
    /// <summary>
    /// Adds a junction-level logger that records junction names, durations, and optionally serialized input/output
    /// for each junction in a train. Log entries are written at the configured effect log level.
    /// </summary>
    /// <typeparam name="TBuilder">The builder type (supports chaining through promoted builders).</typeparam>
    /// <param name="configurationBuilder">The effect builder.</param>
    /// <param name="serializeJunctionData">
    /// When <c>true</c>, junction input and output are serialized to JSON and included in log entries.
    /// Defaults to <c>false</c> to avoid performance overhead.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddJunctionLogger<TBuilder>(
        this TBuilder configurationBuilder,
        bool serializeJunctionData = false
    )
        where TBuilder : TraxEffectBuilder
    {
        configurationBuilder.SerializeJunctionData = serializeJunctionData;
        configurationBuilder.ServiceCollection.AddTransient<
            IJunctionLoggerProvider,
            JunctionLoggerProvider
        >();

        configurationBuilder.AddJunctionEffect<JunctionLoggerFactory>();
        return configurationBuilder;
    }
}
