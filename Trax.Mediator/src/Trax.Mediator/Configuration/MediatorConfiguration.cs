using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Mediator.Configuration;

/// <summary>
/// Configuration captured by <see cref="TraxMediatorBuilder"/> during <c>AddMediator()</c>.
/// Registered as a singleton in DI.
/// </summary>
public class MediatorConfiguration
{
    /// <summary>
    /// The DI lifetime for discovered train implementations.
    /// </summary>
    public ServiceLifetime TrainLifetime { get; internal set; } = ServiceLifetime.Transient;

    /// <summary>
    /// The assemblies scanned for <c>IServiceTrain&lt;TIn, TOut&gt;</c> implementations.
    /// </summary>
    public Assembly[] Assemblies { get; internal set; } = [];

    /// <summary>
    /// Global maximum concurrent RUN executions across all trains.
    /// Null means no global limit.
    /// </summary>
    public int? GlobalMaxConcurrentRun { get; internal set; }

    /// <summary>
    /// Per-train concurrency overrides from the builder, keyed by interface FullName.
    /// Takes precedence over <see cref="Trax.Effect.Attributes.TraxConcurrencyLimitAttribute"/> values.
    /// </summary>
    internal Dictionary<string, int> ConcurrencyOverrides { get; } = new();
}
