using System.Text.Json;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Trax.Effect.Configuration.Trax.CoreEffectBuilder;

public class Trax.CoreEffectConfigurationBuilder(
    IServiceCollection serviceCollection,
    IEffectRegistry? effectRegistry = null
)
{
    public IServiceCollection ServiceCollection => serviceCollection;

    public IEffectRegistry? EffectRegistry => effectRegistry;

    public bool DataContextLoggingEffectEnabled { get; set; } = false;

    public bool SerializeStepData { get; set; } = false;

    public LogLevel LogLevel { get; set; } = LogLevel.Debug;

    public JsonSerializerOptions WorkflowParameterJsonSerializerOptions { get; set; } =
        Trax.CoreJsonSerializationOptions.Default;

    public JsonSerializerSettings NewtonsoftJsonSerializerSettings { get; set; } =
        Trax.CoreJsonSerializationOptions.NewtonsoftDefault;

    protected internal Trax.CoreEffectConfiguration.Trax.CoreEffectConfiguration Build()
    {
        var configuration = new Trax.CoreEffectConfiguration.Trax.CoreEffectConfiguration
        {
            SystemJsonSerializerOptions = WorkflowParameterJsonSerializerOptions,
            NewtonsoftJsonSerializerSettings = NewtonsoftJsonSerializerSettings,
            SerializeStepData = SerializeStepData,
            LogLevel = LogLevel,
        };

        Trax.CoreEffectConfiguration
            .Trax.CoreEffectConfiguration
            .StaticSystemJsonSerializerOptions = WorkflowParameterJsonSerializerOptions;

        return configuration;
    }
}
