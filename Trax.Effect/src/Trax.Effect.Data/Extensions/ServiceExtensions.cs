using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContextLoggingProvider;
using TraxEffectBuilder = Trax.Effect.Configuration.TraxEffectBuilder.TraxEffectBuilder;

namespace Trax.Effect.Data.Extensions;

/// <summary>
/// Provides extension methods for configuring Trax.Effect.Data services in the dependency injection container.
/// </summary>
/// <remarks>
/// The ServiceExtensions class contains utility methods that simplify the registration
/// of Trax.Effect.Data services with the dependency injection system.
///
/// These extensions enable:
/// 1. Easy configuration of data context logging
/// 2. Consistent service registration across different applications
/// 3. Integration with the Trax.Effect configuration system
///
/// By using these extensions, applications can easily configure and use the
/// Trax.Effect.Data system with minimal boilerplate code.
/// </remarks>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds data context logging to the Trax.Effect system.
    /// </summary>
    /// <param name="configurationBuilder">The Trax.Core effect configuration builder</param>
    /// <param name="minimumLogLevel">The minimum log level to capture (defaults to Information if not specified)</param>
    /// <param name="blacklist">A list of namespace patterns to exclude from logging</param>
    /// <returns>The configuration builder for method chaining</returns>
    /// <exception cref="Exception">Thrown if data context logging is not enabled</exception>
    /// <remarks>
    /// This method configures logging for database operations in the Trax.Effect.Data system.
    /// It registers the necessary services for capturing and processing database logs.
    ///
    /// The method:
    /// 1. Verifies that data context logging is enabled in the configuration
    /// 2. Creates a logging configuration with the specified settings
    /// 3. Registers the logging provider and configuration with the dependency injection container
    ///
    /// Data context logging provides visibility into:
    /// - SQL queries executed
    /// - Transaction boundaries
    /// - Errors and warnings
    ///
    /// This is particularly useful for debugging and performance optimization.
    ///
    /// Example usage:
    /// ```csharp
    /// services.AddTrax(trax => trax
    ///     .AddEffects(effects => effects
    ///         .UsePostgres(connectionString)
    ///         .AddDataContextLogging(
    ///             minimumLogLevel: LogLevel.Information,
    ///             blacklist: ["Microsoft.EntityFrameworkCore.*"]
    ///         )
    ///     )
    /// );
    /// ```
    /// </remarks>
    public static TraxEffectBuilder AddDataContextLogging(
        this TraxEffectBuilder configurationBuilder,
        LogLevel? minimumLogLevel = null,
        List<string>? blacklist = null
    )
    {
        // Verify that data context logging is enabled
        if (configurationBuilder.DataContextLoggingEffectEnabled == false)
            throw new Exception(
                "Data Context Logging effect is not enabled in Trax.Core. Ensure a Data Effect has been added to TraxEffects (before calling AddDataContextLogging). e.g. .AddTrax(trax => trax.AddEffects(effects => effects.UsePostgres(connectionString).AddDataContextLogging()))"
            );

        // Create and register the logging configuration
        var credentials = new DataContextLoggingProviderConfiguration
        {
            MinimumLogLevel = minimumLogLevel ?? LogLevel.Information,
            Blacklist = blacklist ?? [],
        };

        configurationBuilder
            .ServiceCollection.AddSingleton<IDataContextLoggingProviderConfiguration>(credentials)
            .AddSingleton<ILoggerProvider, DataContextLoggingProvider>();

        return configurationBuilder;
    }
}
