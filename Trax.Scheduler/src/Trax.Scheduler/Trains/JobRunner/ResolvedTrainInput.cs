namespace Trax.Scheduler.Trains.JobRunner;

/// <summary>
/// Wraps the resolved train input for type-safe routing through Trax.Core's Memory system.
/// </summary>
/// <remarks>
/// Trax.Core stores tuple elements by runtime type but reconstructs tuples by declared generic type.
/// Using raw <c>object</c> as a tuple element won't resolve from Memory. This wrapper provides
/// a concrete type for Memory routing when passing train input through the JobRunner chain.
/// </remarks>
/// <param name="Value">The train input object</param>
public record ResolvedTrainInput(object Value);
