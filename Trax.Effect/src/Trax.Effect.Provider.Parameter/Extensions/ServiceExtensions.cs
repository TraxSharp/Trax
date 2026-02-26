using System.Text.Json;
using Trax.Effect.Configuration.Trax.CoreEffectBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Parameter.Configuration;
using Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Effect.Provider.Parameter.Extensions;

/// <summary>
/// Provides extension methods for configuring Trax.Effect.Provider.Parameter services in the dependency injection container.
/// </summary>
/// <remarks>
/// The ServiceExtensions class contains utility methods that simplify the registration
/// of Trax.Effect.Provider.Parameter services with the dependency injection system.
///
/// These extensions enable parameter serialization support for the Trax.Effect system,
/// allowing workflow input and output parameters to be serialized to and from JSON format.
///
/// By using these extensions, applications can easily configure and use the
/// Trax.Effect.Provider.Parameter system with minimal boilerplate code.
/// </remarks>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds parameter serialization support to the Trax.Core effect configuration builder.
    /// </summary>
    /// <param name="builder">The Trax.Core effect configuration builder</param>
    /// <param name="jsonSerializerOptions">Optional JSON serializer options to use for parameter serialization</param>
    /// <returns>The configuration builder for method chaining</returns>
    /// <remarks>
    /// This method configures the Trax.Effect system to serialize workflow input and output
    /// parameters to JSON format. It registers the necessary services with the dependency
    /// injection container and configures the JSON serialization options.
    ///
    /// The method performs the following steps:
    /// 1. Sets the JSON serializer options to use for parameter serialization
    /// 2. Registers the ParameterEffectProviderFactory as an IEffectProviderFactory
    ///
    /// If no JSON serializer options are provided, the default options from Trax.CoreJsonSerializationOptions
    /// are used. These default options are configured to handle common serialization scenarios
    /// in the Trax.Effect system.
    ///
    /// Example usage:
    /// ```csharp
    /// services.AddTrax.CoreEffects(options =>
    ///     options.SaveWorkflowParameters()
    /// );
    /// ```
    ///
    /// Or with custom JSON serializer options:
    /// ```csharp
    /// var jsonOptions = new JsonSerializerOptions
    /// {
    ///     PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    ///     WriteIndented = true
    /// };
    ///
    /// services.AddTrax.CoreEffects(options =>
    ///     options.SaveWorkflowParameters(jsonOptions)
    /// );
    /// ```
    ///
    /// Or with parameter configuration to control which parameters are saved:
    /// ```csharp
    /// services.AddTrax.CoreEffects(options =>
    ///     options.SaveWorkflowParameters(configure: cfg =>
    ///     {
    ///         cfg.SaveInputs = true;
    ///         cfg.SaveOutputs = false;
    ///     })
    /// );
    /// ```
    /// </remarks>
    public static Trax.CoreEffectConfigurationBuilder SaveWorkflowParameters(
        this Trax.CoreEffectConfigurationBuilder builder,
        JsonSerializerOptions? jsonSerializerOptions = null,
        Action<ParameterEffectConfiguration>? configure = null
    )
    {
        jsonSerializerOptions ??= Trax.CoreJsonSerializationOptions.Default;

        var effectConfiguration = new ParameterEffectConfiguration();
        configure?.Invoke(effectConfiguration);

        builder.ServiceCollection.AddSingleton(effectConfiguration);
        builder.WorkflowParameterJsonSerializerOptions = jsonSerializerOptions;

        return builder.AddEffect<ParameterEffectProviderFactory>();
    }
}
