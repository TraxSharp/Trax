using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.Configuration.TraxEffectConfiguration;

/// <summary>
/// The effect settings built from <c>AddEffects(...)</c>, registered as a singleton. Effect providers inject it to
/// learn how to serialize train parameters, whether to capture junction output, and which level to log at.
/// </summary>
public interface ITraxEffectConfiguration
{
    /// <summary>
    /// The serializer options for train input and output parameters: the options passed to
    /// <c>SaveTrainParameters()</c>, or <see cref="Utils.TraxJsonSerializationOptions.Default"/> when none were given.
    /// </summary>
    public JsonSerializerOptions SystemJsonSerializerOptions { get; }

    /// <summary>
    /// Whether the junction logger stores each junction's serialized output in <c>JunctionMetadata.OutputJson</c>.
    /// <c>false</c> unless <c>AddJunctionLogger(serializeJunctionData: true)</c> was called; while it is
    /// <c>false</c>, <c>OutputJson</c> is left <c>null</c>.
    /// </summary>
    public bool SerializeJunctionData { get; }

    /// <summary>
    /// The level the junction logger and the JSON effect log at. Defaults to <see cref="LogLevel.Debug"/>.
    /// </summary>
    public LogLevel LogLevel { get; }
}
