using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckFactory;
using Trax.Effect.JunctionProvider.Progress.Services.CancellationCheckProvider;
using Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressFactory;
using Trax.Effect.JunctionProvider.Progress.Services.JunctionProgressProvider;
using TraxEffectBuilder = Trax.Effect.Configuration.TraxEffectBuilder.TraxEffectBuilder;

namespace Trax.Effect.JunctionProvider.Progress.Extensions;

/// <summary>
/// Adds <c>AddJunctionProgress</c> to the effect builder, which records which junction a run is on and
/// lets a requested cancellation stop the run between junctions.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds junction progress tracking and cancellation checking. Before each junction runs, the run's
    /// metadata row is read, and if its <c>CancellationRequested</c> flag has been set (by the dashboard
    /// or the API) the run stops with an <see cref="OperationCanceledException"/>. Otherwise the junction's
    /// name and start time are saved to the metadata's <c>CurrentlyRunningJunction</c> and
    /// <c>JunctionStartedAt</c>, and cleared again after it finishes.
    /// Requires a data provider (<c>UsePostgres()</c>, <c>UseSqlite()</c>, or <c>UseInMemory()</c>).
    /// </summary>
    /// <typeparam name="TBuilder">The builder type (supports chaining through promoted builders).</typeparam>
    /// <param name="configurationBuilder">The effect builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddJunctionProgress<TBuilder>(this TBuilder configurationBuilder)
        where TBuilder : TraxEffectBuilder
    {
        configurationBuilder.JunctionProgressEnabled = true;

        // Register CancellationCheck FIRST so it runs before JunctionProgress sets columns
        configurationBuilder.AddJunctionEffect<CancellationCheckFactory>();
        configurationBuilder.AddJunctionEffect<JunctionProgressFactory>();
        return configurationBuilder;
    }
}
