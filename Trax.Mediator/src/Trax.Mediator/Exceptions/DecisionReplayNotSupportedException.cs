namespace Trax.Mediator.Exceptions;

/// <summary>
/// Thrown by the default <c>ITrainExecutionService.QueueAsync</c> overload that takes
/// <see cref="Services.TrainExecution.QueueTrainOptions"/> when
/// <see cref="Services.TrainExecution.QueueTrainOptions.ReplayDecisionsOf"/> is set and the
/// registered implementation does not implement that overload: a custom
/// <c>ITrainExecutionService</c>, or a decorator around the mediator's, written before the
/// overload existed.
/// </summary>
/// <remarks>
/// This is a misconfiguration of the host, not a refusal of the caller or of the input, so a
/// surface that reports enqueue refusals to clients can report it as a server fault instead, as it
/// does <see cref="TrainAuthorizationNotConfiguredException"/>. The enqueue is not made without the
/// replay link, because the run would then ask its deciders afresh and could take a different
/// track from the run it repeats. It derives from <see cref="NotSupportedException"/>, the type
/// thrown here before it existed, so existing catches still see it.
/// </remarks>
public class DecisionReplayNotSupportedException : NotSupportedException
{
    /// <summary>The registered <c>ITrainExecutionService</c> that lacks the overload.</summary>
    public Type ImplementationType { get; }

    /// <summary>Creates the exception for the implementation that lacks the overload.</summary>
    /// <param name="implementationType">The registered implementation's type.</param>
    public DecisionReplayNotSupportedException(Type implementationType)
        : base(
            $"{implementationType.FullName} does not implement the ITrainExecutionService.QueueAsync "
                + "overload that takes QueueTrainOptions, so it cannot queue a run that replays an "
                + "earlier run's decisions. A custom or decorating ITrainExecutionService must "
                + "implement that overload and pass ReplayDecisionsOf through."
        )
    {
        ImplementationType = implementationType;
    }
}
