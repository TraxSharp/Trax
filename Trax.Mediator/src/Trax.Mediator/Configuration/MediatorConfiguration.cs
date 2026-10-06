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
    /// When true, the host does not read every registered train's chain at startup.
    /// </summary>
    /// <remarks>
    /// The check is on by default: a chain is a declaration, so whether it can run is decidable
    /// before any traffic arrives. There are two reasons to turn it off. One is the blind spot
    /// named in <c>ChainVerification</c>, where a junction declares an interface that the value
    /// flowing in implements only incidentally. The other is temporary: a codebase being moved
    /// onto <c>Junctions()</c> whose chains do not pass yet. The check of each train's
    /// <c>[Inject]</c> properties is part of it and is off with it. Set with
    /// <c>TraxMediatorBuilder.SkipChainVerification()</c>.
    /// </remarks>
    public bool SkipChainVerification { get; internal set; }

    /// <summary>
    /// Maximum UTF-8 byte length for caller-supplied train input JSON in
    /// <c>ITrainExecutionService.RunAsync</c> / <c>QueueAsync</c>. Defaults to
    /// 256 KiB. Override via <c>TraxMediatorBuilder.WithMaxInputJsonBytes(int)</c>.
    /// </summary>
    /// <remarks>
    /// The cap is enforced post-authorization but pre-deserialization, so oversized JSON never
    /// reaches the deserializer and no fail-closed check is skipped for it. Caller input is read
    /// without JSON reference handling (<c>$id</c>, <c>$ref</c>, <c>$values</c> are not
    /// honoured), so the parsed input is the tree the caller wrote.
    /// <para>
    /// A work queue entry created by <c>QueueAsync</c> stores the input re-serialized from the
    /// parsed object, indented and with every member present, which is larger than compact caller
    /// JSON. That stored form has a cap of its own,
    /// <see cref="Services.TrainExecution.TrainInputReader.StoredInputGrowthFactor"/> times this
    /// value, checked before the entry is created; an enqueue whose stored input would be larger
    /// is refused with <see cref="Exceptions.TrainInputValidationException"/>.
    /// </para>
    /// </remarks>
    public int MaxInputJsonBytes { get; internal set; } = 262_144;

    /// <summary>
    /// Maximum concurrent RUN executions per authenticated principal. When the
    /// limit is reached, additional requests from the same principal queue on a
    /// per-principal semaphore until an in-flight request completes. Defaults to
    /// <c>null</c> (no per-principal cap — global and per-train limits still apply).
    /// Principals are identified by
    /// <see cref="Services.Principal.ICurrentPrincipalProvider"/>. The default registered by
    /// <c>AddMediator</c> returns <c>null</c>, so the cap has no effect until a provider that
    /// returns an id is registered, as <c>AddTraxApi</c> does. A run with no principal id is not
    /// capped per principal. Queued work is not counted.
    /// </summary>
    public int? PerPrincipalMaxConcurrentRun { get; internal set; }

    /// <summary>
    /// How long an <c>OnQueue</c> hook may run while its enqueue holds a connection and an open
    /// transaction. Past it the enqueue fails with
    /// <see cref="Exceptions.QueueHookTimeoutException"/>, rolls back what the hook wrote, and
    /// releases the connection. Defaults to 30 seconds; <see cref="Timeout.InfiniteTimeSpan"/>
    /// removes the limit. Set via <c>TraxMediatorBuilder.WithMaxQueueHookDuration(TimeSpan)</c>.
    /// </summary>
    /// <remarks>
    /// The hook's token is cancelled at the limit, but a hook that ignores it keeps running after
    /// its enqueue has failed, without the enqueue's context or transaction. An enqueue it starts
    /// after that is refused. A deferring train's hook holds no connection while it runs and is
    /// not limited; the stale-staged sweep bounds it instead.
    /// </remarks>
    public TimeSpan MaxQueueHookDuration { get; internal set; } = DefaultMaxQueueHookDuration;

    /// <summary>The default <see cref="MaxQueueHookDuration"/>: 30 seconds.</summary>
    internal static readonly TimeSpan DefaultMaxQueueHookDuration = TimeSpan.FromSeconds(30);
}
