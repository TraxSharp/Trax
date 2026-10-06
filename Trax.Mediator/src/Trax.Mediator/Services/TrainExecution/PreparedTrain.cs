using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.TrainExecution;

/// <summary>
/// A train a caller named, authorized for the current caller, with the caller's input read into
/// the train's input type. Only <see cref="ITrainExecutionService.PrepareAsync"/> produces one, so
/// holding one means the authorization check ran.
/// </summary>
public sealed class PreparedTrain
{
    internal PreparedTrain(TrainRegistration registration, object input)
    {
        Registration = registration;
        Input = input;
    }

    /// <summary>The train the name resolved to.</summary>
    public TrainRegistration Registration { get; }

    /// <summary>
    /// The input, an instance of <see cref="TrainRegistration.InputType"/>. Never null.
    /// </summary>
    public object Input { get; }
}
