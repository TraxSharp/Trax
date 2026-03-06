using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Utils;

namespace Trax.Effect.Configuration.TraxEffectBuilder;

/// <summary>
/// Builder for configuring the Trax effect system (data providers, step providers, lifecycle hooks).
/// </summary>
public class TraxEffectBuilder
{
    private readonly TraxBuilder.TraxBuilder _parent;

    internal TraxEffectBuilder(TraxBuilder.TraxBuilder parent)
    {
        _parent = parent;
    }

    public IServiceCollection ServiceCollection => _parent.ServiceCollection;

    public IEffectRegistry? EffectRegistry => _parent.EffectRegistry;

    public bool DataContextLoggingEffectEnabled { get; set; } = false;

    public bool SerializeStepData { get; set; } = false;

    public LogLevel LogLevel { get; set; } = LogLevel.Debug;

    public JsonSerializerOptions TrainParameterJsonSerializerOptions { get; set; } =
        TraxJsonSerializationOptions.Default;

    public JsonSerializerSettings NewtonsoftJsonSerializerSettings { get; set; } =
        TraxJsonSerializationOptions.NewtonsoftDefault;

    internal TraxEffectConfiguration.TraxEffectConfiguration Build()
    {
        var configuration = new TraxEffectConfiguration.TraxEffectConfiguration
        {
            SystemJsonSerializerOptions = TrainParameterJsonSerializerOptions,
            NewtonsoftJsonSerializerSettings = NewtonsoftJsonSerializerSettings,
            SerializeStepData = SerializeStepData,
            LogLevel = LogLevel,
        };

        TraxEffectConfiguration.TraxEffectConfiguration.StaticSystemJsonSerializerOptions =
            TrainParameterJsonSerializerOptions;

        return configuration;
    }
}
