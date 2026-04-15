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

    /// <summary>
    /// When <c>true</c>, the host may start without registering an
    /// <c>ITrainAuthorizationService</c> even though some trains carry
    /// <c>[TraxAuthorize]</c>. Intended for scheduler-only or dashboard-only processes
    /// that never accept API submissions. Opt in via
    /// <c>TraxMediatorBuilder.AllowMissingAuthorizationService()</c>.
    /// </summary>
    public bool AllowMissingAuthorizationService { get; internal set; }

    /// <summary>
    /// Maximum UTF-8 byte length for caller-supplied train input JSON in
    /// <c>ITrainExecutionService.RunAsync</c> / <c>QueueAsync</c>. Defaults to
    /// 256 KiB. Override via <c>TraxMediatorBuilder.WithMaxInputJsonBytes(int)</c>.
    /// </summary>
    /// <remarks>
    /// The cap is enforced post-authorization but pre-deserialization so that
    /// attacker-controlled JSON cannot exhaust memory or trigger deserializer
    /// gadget chains before any fail-closed check has run. Queued work entries
    /// created by <c>QueueAsync</c> are re-serialized from the parsed CLR object
    /// and are therefore governed by the same cap indirectly.
    /// </remarks>
    public int MaxInputJsonBytes { get; internal set; } = 262_144;

    /// <summary>
    /// Maximum concurrent RUN executions per authenticated principal. When the
    /// limit is reached, additional requests from the same principal queue on a
    /// per-principal semaphore until an in-flight request completes. Defaults to
    /// <c>null</c> (no per-principal cap — global and per-train limits still apply).
    /// Requires <c>IHttpContextAccessor</c> to be registered; without it the cap
    /// has no effect.
    /// </summary>
    public int? PerPrincipalMaxConcurrentRun { get; internal set; }
}
