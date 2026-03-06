using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Extensions;
using Trax.Effect.StepProvider.Logging.Services.StepLoggerFactory;
using Trax.Effect.StepProvider.Logging.Services.StepLoggerProvider;
using TraxEffectBuilder = Trax.Effect.Configuration.TraxEffectBuilder.TraxEffectBuilder;

namespace Trax.Effect.StepProvider.Logging.Extensions;

public static class ServiceExtensions
{
    public static TraxEffectBuilder AddStepLogger(
        this TraxEffectBuilder configurationBuilder,
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
