using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckFactory;
using Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckProvider;
using Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressFactory;
using Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressProvider;
using TraxEffectBuilder = Trax.Effect.Configuration.TraxEffectBuilder.TraxEffectBuilder;

namespace Trax.Effect.JunctionProvider.Progress.Extensions;

public static class ServiceExtensions
{
    /// <summary>
    /// Adds junction progress tracking and cancellation checking. Each junction's progress
    /// (current junction index, total junctions, junction name) is persisted to metadata, and
    /// the train's cancellation token is checked before each junction executes.
    /// Requires a data provider (<c>UsePostgres()</c> or <c>UseInMemory()</c>).
    /// </summary>
    /// <typeparam name="TBuilder">The builder type (supports chaining through promoted builders).</typeparam>
    /// <param name="configurationBuilder">The effect builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddJunctionProgress<TBuilder>(this TBuilder configurationBuilder)
        where TBuilder : TraxEffectBuilder
    {
        configurationBuilder.JunctionProgressEnabled = true;

        configurationBuilder.ServiceCollection.AddTransient<
            ICancellationCheckProvider,
            CancellationCheckProvider
        >();
        configurationBuilder.ServiceCollection.AddTransient<
            IJunctionProgressProvider,
            JunctionProgressProvider
        >();

        // Register CancellationCheck FIRST so it runs before JunctionProgress sets columns
        configurationBuilder.AddJunctionEffect<CancellationCheckFactory>();
        configurationBuilder.AddJunctionEffect<JunctionProgressFactory>();
        return configurationBuilder;
    }
}
