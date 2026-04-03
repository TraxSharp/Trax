using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.Services.DataContextLoggingProvider;

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
    /// Requires a data provider (<c>UsePostgres()</c>, <c>UseSqlite()</c>, or <c>UseInMemory()</c>) to have been configured first.
    /// </summary>
    /// <param name="configurationBuilder">
    /// The effect builder with a data provider configured. This method is only available
    /// after calling <c>UsePostgres()</c>, <c>UseSqlite()</c>, or <c>UseInMemory()</c>, which promotes the builder
    /// to <see cref="TraxEffectBuilderWithData"/>.
    /// </param>
    /// <param name="minimumLogLevel">The minimum log level to capture (defaults to Information if not specified)</param>
    /// <param name="blacklist">A list of namespace patterns to exclude from logging</param>
    /// <returns>The configuration builder for method chaining</returns>
    /// <remarks>
    /// This method configures logging for database operations in the Trax.Effect.Data system.
    /// It registers the necessary services for capturing and processing database logs.
    ///
    /// The method:
    /// 1. Creates a logging configuration with the specified settings
    /// 2. Registers the logging provider and configuration with the dependency injection container
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
    ///
    /// Calling this method without a data provider will result in a compile-time error,
    /// since <c>AddDataContextLogging()</c> is only defined on <see cref="TraxEffectBuilderWithData"/>.
    /// </remarks>
    public static TraxEffectBuilderWithData AddDataContextLogging(
        this TraxEffectBuilderWithData configurationBuilder,
        LogLevel? minimumLogLevel = null,
        List<string>? blacklist = null
    )
    {
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
