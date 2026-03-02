using LanguageExt;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Scheduler.Services.DormantDependentContext;

/// <summary>
/// Scoped service for activating dormant dependent manifests at runtime.
/// </summary>
/// <remarks>
/// Injected into train steps that need to selectively fire dependent trains
/// with runtime-determined input. Only dormant dependents declared as children of
/// the currently executing parent manifest can be activated.
///
/// The context is automatically initialized by the TaskServerExecutor before the
/// user's train runs. If called outside of a scheduled execution (no manifest
/// context), all calls will throw <see cref="InvalidOperationException"/>.
///
/// <example>
/// <code>
/// public class MyStep(IDormantDependentContext dormants)
///     : Step&lt;MyInput, Unit&gt;
/// {
///     public override async Task&lt;Unit&gt; Run(MyInput input)
///     {
///         await dormants.ActivateAsync&lt;IChildTrain, ChildInput&gt;(
///             "child-external-id",
///             new ChildInput { Data = input.RuntimeData });
///         return Unit.Default;
///     }
/// }
/// </code>
/// </example>
/// </remarks>
public interface IDormantDependentContext
{
    /// <summary>
    /// Activates a single dormant dependent manifest, creating a WorkQueue entry
    /// with the provided runtime input.
    /// </summary>
    /// <typeparam name="TTrain">The train interface type of the dormant dependent.</typeparam>
    /// <typeparam name="TInput">The input type for the train.</typeparam>
    /// <param name="externalId">The external ID of the dormant dependent manifest to activate.</param>
    /// <param name="input">The runtime-determined input for the dependent train.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// If the target manifest already has a queued WorkQueue entry or an active execution
    /// (Pending/InProgress Metadata), the activation is silently skipped to prevent
    /// duplicate work. A warning is logged in this case.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when:
    /// <list type="bullet">
    /// <item>The context has not been initialized (not running inside a scheduled execution)</item>
    /// <item>No manifest with the specified external ID exists</item>
    /// <item>The target manifest is not a DormantDependent</item>
    /// <item>The target manifest does not depend on the current parent manifest</item>
    /// </list>
    /// </exception>
    Task ActivateAsync<TTrain, TInput>(
        string externalId,
        TInput input,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties;

    /// <summary>
    /// Activates multiple dormant dependent manifests in a single transaction.
    /// </summary>
    /// <typeparam name="TTrain">The train interface type of the dormant dependents.</typeparam>
    /// <typeparam name="TInput">The input type for the trains.</typeparam>
    /// <param name="activations">
    /// Collection of (ExternalId, Input) pairs identifying which dormant dependents
    /// to activate and with what input.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// All activations are performed in a single database transaction. If any validation
    /// fails (wrong parent, not dormant, etc.), the entire batch is rolled back.
    /// Concurrency-skipped entries (already queued/active) do not cause a rollback.
    /// </remarks>
    Task ActivateManyAsync<TTrain, TInput>(
        IEnumerable<(string ExternalId, TInput Input)> activations,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties;
}
