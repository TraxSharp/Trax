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
    public static TraxEffectBuilder AddStepProgress(this TraxEffectBuilder configurationBuilder)
    {
        configurationBuilder.ServiceCollection.AddTransient<
            ICancellationCheckProvider,
            CancellationCheckProvider
        >();
        configurationBuilder.ServiceCollection.AddTransient<
            IStepProgressProvider,
            StepProgressProvider
        >();

        // Register CancellationCheck FIRST so it runs before StepProgress sets columns
        return configurationBuilder
            .AddStepEffect<CancellationCheckFactory>()
            .AddStepEffect<StepProgressFactory>();
    }
}
