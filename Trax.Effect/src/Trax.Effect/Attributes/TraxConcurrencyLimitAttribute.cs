namespace Trax.Effect.Attributes;

/// <summary>
/// Limits the number of concurrent RUN executions for a train.
/// When the limit is reached, additional requests wait until a slot opens.
/// This is useful for protecting remote backends (e.g. AWS Lambda with limited
/// reserved concurrency) from oversubscription.
/// </summary>
/// <remarks>
/// Place this attribute on the concrete train class. The limit applies only to
/// synchronous RUN executions (via <c>ITrainExecutionService.RunAsync</c>).
/// Queue operations are not affected since queueing is a lightweight database write.
///
/// The limit can be overridden at the builder level using
/// <c>ConcurrentRunLimit&lt;TTrain&gt;(int)</c> on the mediator builder.
///
/// <example>
/// <code>
/// [TraxConcurrencyLimit(15)]
/// [TraxMutation]
/// public class ResolveCombatTrain : ServiceTrain&lt;CombatInput, CombatResult&gt;, IResolveCombatTrain
/// {
///     // At most 15 concurrent RUN executions of this train
/// }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class TraxConcurrencyLimitAttribute(int maxConcurrent) : Attribute
{
    /// <summary>
    /// The maximum number of concurrent RUN executions allowed for this train.
    /// </summary>
    public int MaxConcurrent { get; } =
        maxConcurrent >= 1
            ? maxConcurrent
            : throw new ArgumentOutOfRangeException(nameof(maxConcurrent), "Must be >= 1");
}
