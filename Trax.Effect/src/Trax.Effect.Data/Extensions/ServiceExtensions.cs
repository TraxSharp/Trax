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
    /// Registers an <see cref="ILoggerProvider"/> that stores the host's <see cref="ILogger"/> messages
    /// in the <c>trax.log</c> table, readable through <c>IDataContext.Logs</c>.
    /// Requires a data provider (<c>UsePostgres()</c>, <c>UseSqlite()</c>, or <c>UseInMemory()</c>) to have been configured first.
    /// </summary>
    /// <param name="configurationBuilder">
    /// The effect builder with a data provider configured. This method is only available
    /// after calling <c>UsePostgres()</c>, <c>UseSqlite()</c>, or <c>UseInMemory()</c>, which promotes the builder
    /// to <see cref="TraxEffectBuilderWithData"/>.
    /// </param>
    /// <param name="minimumLogLevel">The minimum log level to capture (defaults to Information if not specified)</param>
    /// <param name="blacklist">
    /// Logger categories not to store: an exact category name, or a pattern in which <c>*</c> matches
    /// any run of characters.
    /// </param>
    /// <returns>The configuration builder for method chaining</returns>
    /// <remarks>
    /// It is a sink for application logging, not a trace of the data context: every
    /// <see cref="ILogger"/> category at or above <paramref name="minimumLogLevel"/> and not
    /// blacklisted is stored, whatever wrote it. It does not record SQL or transaction boundaries.
    /// EF Core's own command log (<c>Microsoft.EntityFrameworkCore.Database.Command</c>) is always
    /// skipped, because writing a row would log another one.
    ///
    /// Entries are queued in memory (4096, oldest dropped when full) and written in batches by
    /// <see cref="DataContextLoggingProvider"/>, which stores what is queued when the host stops.
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
