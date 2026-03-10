using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Effect.StepProvider.Progress.Services.CancellationCheckFactory;
using Trax.Effect.StepProvider.Progress.Services.CancellationCheckProvider;
using Trax.Effect.StepProvider.Progress.Services.StepProgressFactory;
using Trax.Effect.StepProvider.Progress.Services.StepProgressProvider;
using TraxEffectBuilder = Trax.Effect.Configuration.TraxEffectBuilder.TraxEffectBuilder;

namespace Trax.Effect.StepProvider.Progress.Extensions;

public static class ServiceExtensions
{
    /// <summary>
    /// Adds step progress tracking and cancellation checking. Each step's progress
    /// (current step index, total steps, step name) is persisted to metadata, and
    /// the train's cancellation token is checked before each step executes.
    /// Requires a data provider (<c>UsePostgres()</c> or <c>UseInMemory()</c>).
    /// </summary>
    /// <typeparam name="TBuilder">The builder type (supports chaining through promoted builders).</typeparam>
    /// <param name="configurationBuilder">The effect builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static TBuilder AddStepProgress<TBuilder>(this TBuilder configurationBuilder)
        where TBuilder : TraxEffectBuilder
    {
        configurationBuilder.StepProgressEnabled = true;

        configurationBuilder.ServiceCollection.AddTransient<
            ICancellationCheckProvider,
            CancellationCheckProvider
        >();
        configurationBuilder.ServiceCollection.AddTransient<
            IStepProgressProvider,
            StepProgressProvider
        >();

        // Register CancellationCheck FIRST so it runs before StepProgress sets columns
        configurationBuilder.AddStepEffect<CancellationCheckFactory>();
        configurationBuilder.AddStepEffect<StepProgressFactory>();
        return configurationBuilder;
    }
}
