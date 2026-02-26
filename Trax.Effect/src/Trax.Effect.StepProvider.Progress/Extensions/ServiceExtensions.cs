using Trax.Effect.Configuration.Trax.CoreEffectBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.StepProvider.Progress.Services.CancellationCheckFactory;
using Trax.Effect.StepProvider.Progress.Services.CancellationCheckProvider;
using Trax.Effect.StepProvider.Progress.Services.StepProgressFactory;
using Trax.Effect.StepProvider.Progress.Services.StepProgressProvider;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Effect.StepProvider.Progress.Extensions;

public static class ServiceExtensions
{
    public static Trax.CoreEffectConfigurationBuilder AddStepProgress(
        this Trax.CoreEffectConfigurationBuilder configurationBuilder
    )
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
