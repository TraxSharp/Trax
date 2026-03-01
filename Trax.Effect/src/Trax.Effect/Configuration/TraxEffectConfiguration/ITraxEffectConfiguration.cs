using System.Text.Json;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Trax.Effect.Configuration.TraxEffectConfiguration;

/// <summary>
/// There is unfortunate use cases for both Newtonsoft.Json and System.Text.Json.
///
/// The overwhelming majority of the solution uses System.Text.Json for Serialization as it is
/// much faster, and provides more granularity when it comes to serialization/deserialization.
///
/// The only current use case of Newtonsoft is Serializing arbitrary output objects in steps
/// due to its' ability to handle IDisposable objects and Circular Dependencies much better.
/// </summary>
public interface ITraxEffectConfiguration
{
    /// <summary>
    /// System.Text.Json Serialization Options
    /// </summary>
    public JsonSerializerOptions SystemJsonSerializerOptions { get; }

    /// <summary>
    /// Newtonsoft.Json Serialization Options
    /// </summary>
    public JsonSerializerSettings NewtonsoftJsonSerializerSettings { get; }

    public bool SerializeStepData { get; }

    public LogLevel LogLevel { get; }
}
