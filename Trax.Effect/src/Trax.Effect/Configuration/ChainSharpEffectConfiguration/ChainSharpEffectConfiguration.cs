using System.Text.Json;
using Trax.Effect.Utils;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Trax.Effect.Configuration.Trax.CoreEffectConfiguration;

public class Trax.CoreEffectConfiguration : ITrax.CoreEffectConfiguration
{
    public JsonSerializerOptions SystemJsonSerializerOptions { get; set; } =
        Trax.CoreJsonSerializationOptions.Default;

    public JsonSerializerSettings NewtonsoftJsonSerializerSettings { get; set; } =
        Trax.CoreJsonSerializationOptions.NewtonsoftDefault;

    public static JsonSerializerOptions StaticSystemJsonSerializerOptions { get; set; } =
        JsonSerializerOptions.Default;

    public bool SerializeStepData { get; set; } = false;

    public LogLevel LogLevel { get; set; } = LogLevel.Debug;
}
