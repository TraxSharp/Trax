using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Utils;

namespace Trax.Effect.Configuration.TraxEffectBuilder;

public class TraxEffectConfigurationBuilder(
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
        TraxJsonSerializationOptions.Default;

    public JsonSerializerSettings NewtonsoftJsonSerializerSettings { get; set; } =
        TraxJsonSerializationOptions.NewtonsoftDefault;

    protected internal TraxEffectConfiguration.TraxEffectConfiguration Build()
    {
        var configuration = new TraxEffectConfiguration.TraxEffectConfiguration
        {
            SystemJsonSerializerOptions = WorkflowParameterJsonSerializerOptions,
            NewtonsoftJsonSerializerSettings = NewtonsoftJsonSerializerSettings,
            SerializeStepData = SerializeStepData,
            LogLevel = LogLevel,
        };

        TraxEffectConfiguration.TraxEffectConfiguration.StaticSystemJsonSerializerOptions =
            WorkflowParameterJsonSerializerOptions;

        return configuration;
    }
}
