using Trax.Effect.Configuration.Trax.CoreEffectBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.StepProvider.Logging.Services.StepLoggerFactory;
using Trax.Effect.StepProvider.Logging.Services.StepLoggerProvider;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Effect.StepProvider.Logging.Extensions;

public static class ServiceExtensions
{
    public static Trax.CoreEffectConfigurationBuilder AddStepLogger(
        this Trax.CoreEffectConfigurationBuilder configurationBuilder,
        bool serializeStepData = false
    )
    {
        configurationBuilder.SerializeStepData = serializeStepData;
        configurationBuilder.ServiceCollection.AddTransient<
            IStepLoggerProvider,
            StepLoggerProvider
        >();

        return configurationBuilder.AddStepEffect<StepLoggerFactory>();
    }
}
