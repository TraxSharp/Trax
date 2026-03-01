using System.Reflection;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;

namespace Trax.Core.Extensions;

/// <summary>
/// Provides extension methods for working with ILoggerFactory.
/// These methods enable Trax.Core to create loggers dynamically at runtime.
/// </summary>
public static class LoggerExtensions
{
    /// <summary>
    /// Cached generic CreateLogger method to avoid repeated reflection lookups.
    /// </summary>
    private static readonly Lazy<MethodInfo> GenericCreateLoggerMethod = new(() =>
        typeof(LoggerFactoryExtensions)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .First(m =>
                m.Name == nameof(LoggerFactoryExtensions.CreateLogger)
                && m.GetParameters().Length == 1
                && m.IsGenericMethod
            )
    );

    /// <summary>
    /// Creates a generic logger for the specified type.
    /// </summary>
    /// <param name="loggerFactory">The logger factory</param>
    /// <param name="genericType">The type to create a logger for</param>
    /// <returns>A logger for the specified type</returns>
    /// <exception cref="WorkflowException">Thrown if the logger cannot be created</exception>
    /// <remarks>
    /// This method uses reflection to call LoggerFactoryExtensions.CreateLogger&lt;T&gt;
    /// with the specified type. The generic MethodInfo is cached via Lazy&lt;T&gt;.
    /// </remarks>
    public static dynamic CreateGenericLogger(this ILoggerFactory loggerFactory, Type genericType)
    {
        // Make the method generic with the desired type
        var specificMethod = GenericCreateLoggerMethod.Value.MakeGenericMethod(genericType);

        // Invoke the method
        var loggerInstance = specificMethod.Invoke(null, [loggerFactory]);

        if (loggerInstance is null)
            throw new WorkflowException(
                $"Could not create generic logger CreateLogger<({genericType.Name})>."
            );

        return loggerInstance;
    }

    /// <summary>
    /// Gets the logger providers from a logger factory.
    /// </summary>
    /// <param name="loggerFactory">The logger factory</param>
    /// <returns>A list of logger providers</returns>
    /// <remarks>
    /// This method uses reflection to access the private _providers field of the logger factory.
    /// It's used to inspect the logger providers for testing and debugging purposes.
    /// </remarks>
    public static List<ILoggerProvider> GetLoggerProviders(this ILoggerFactory loggerFactory)
    {
        var field = loggerFactory
            .GetType()
            .GetField("_providers", BindingFlags.NonPublic | BindingFlags.Instance);

        return field?.GetValue(loggerFactory) as List<ILoggerProvider> ?? [];
    }
}
