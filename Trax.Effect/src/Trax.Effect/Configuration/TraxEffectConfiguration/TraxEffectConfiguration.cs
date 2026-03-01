using System.Text.Json;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Trax.Effect.Utils;

namespace Trax.Effect.Configuration.TraxEffectConfiguration;

public class TraxEffectConfiguration : ITraxEffectConfiguration
{
    public JsonSerializerOptions SystemJsonSerializerOptions { get; set; } =
        TraxJsonSerializationOptions.Default;

    public JsonSerializerSettings NewtonsoftJsonSerializerSettings { get; set; } =
        TraxJsonSerializationOptions.NewtonsoftDefault;

    public static JsonSerializerOptions StaticSystemJsonSerializerOptions { get; set; } =
        JsonSerializerOptions.Default;

    public bool SerializeStepData { get; set; } = false;

    public LogLevel LogLevel { get; set; } = LogLevel.Debug;
}
