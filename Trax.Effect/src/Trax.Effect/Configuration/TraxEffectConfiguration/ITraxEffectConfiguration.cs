using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.Configuration.TraxEffectConfiguration;

public interface ITraxEffectConfiguration
{
    public JsonSerializerOptions SystemJsonSerializerOptions { get; }

    public bool SerializeJunctionData { get; }

    public LogLevel LogLevel { get; }
}
