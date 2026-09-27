using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trax.Effect.Utils;

namespace Trax.Effect.Configuration.TraxEffectConfiguration;

public class TraxEffectConfiguration : ITraxEffectConfiguration
{
    public JsonSerializerOptions SystemJsonSerializerOptions { get; set; } =
        TraxJsonSerializationOptions.Default;

    /// <summary>
    /// The options the models' <c>ToString()</c> overrides serialize with. Building the effect
    /// configuration replaces them with the train parameter options.
    /// </summary>
    /// <remarks>
    /// Starts as <see cref="TraxJsonSerializationOptions.Default"/>, the same default the builder
    /// uses, rather than <see cref="JsonSerializerOptions.Default"/>, which cannot serialize a
    /// <see cref="Type"/>: a model logged before anything was configured made its
    /// <c>ToString()</c> throw.
    /// </remarks>
    public static JsonSerializerOptions StaticSystemJsonSerializerOptions { get; set; } =
        TraxJsonSerializationOptions.Default;

    public bool SerializeJunctionData { get; set; } = false;

    public LogLevel LogLevel { get; set; } = LogLevel.Debug;
}
