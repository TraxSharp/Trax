using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.StepProvider.Logging.Services.StepLoggerFactory;
using Trax.Effect.StepProvider.Logging.Services.StepLoggerProvider;

namespace Trax.Effect.StepProvider.Logging.Extensions;

public static class ServiceExtensions
{
    public static TraxEffectConfigurationBuilder AddStepLogger(
        this TraxEffectConfigurationBuilder configurationBuilder,
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
