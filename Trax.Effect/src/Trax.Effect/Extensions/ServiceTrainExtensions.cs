using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Core.Exceptions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Microsoft.Extensions.Logging;

namespace Trax.Effect.Extensions;

internal static class ServiceTrainExtensions
{
    /// <summary>
    /// Initializes the train metadata in the database and sets the initial state.
    /// </summary>
    internal static async Task<Unit> InitializeServiceTrain<TIn, TOut>(
        this ServiceTrain<TIn, TOut> serviceTrain
    )
    {
        serviceTrain.EffectRunner.AssertLoaded();

        serviceTrain.Logger?.LogTrace("Initializing ({TrainName})", serviceTrain.TrainName);
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = serviceTrain.TrainName,
                ExternalId = serviceTrain.ExternalId,
                Input = null,
                ParentId = serviceTrain.ParentId,
            }
        );

        return await serviceTrain.InitializeServiceTrain(metadata);
    }

    /// <summary>
    /// Initializes the train metadata in the database and sets the initial state.
    /// </summary>
    internal static async Task<Unit> InitializeServiceTrain<TIn, TOut>(
        this ServiceTrain<TIn, TOut> serviceTrain,
        Metadata metadata
    )
    {
        serviceTrain.EffectRunner.AssertLoaded();

        if (metadata.WorkflowState != WorkflowState.Pending)
            throw new WorkflowException(
                $"Cannot start a train with state ({metadata.WorkflowState}), must be Pending."
            );

        await serviceTrain.EffectRunner.Track(metadata);
        serviceTrain.Logger?.LogTrace("Initializing ({TrainName})", serviceTrain.TrainName);
        serviceTrain.Metadata = metadata;

        return await serviceTrain.StartServiceTrain(metadata);
    }

    internal static async Task<Unit> StartServiceTrain<TIn, TOut>(
        this ServiceTrain<TIn, TOut> serviceTrain,
        Metadata metadata
    )
    {
        serviceTrain.EffectRunner.AssertLoaded();
        serviceTrain.Metadata.AssertLoaded();

        if (metadata.WorkflowState != WorkflowState.Pending)
            throw new WorkflowException(
                $"Cannot start a train with state ({metadata.WorkflowState}), must be Pending."
            );

        serviceTrain.Logger?.LogTrace(
            "Setting ({TrainName}) to In Progress.",
            serviceTrain.TrainName
        );
        serviceTrain.Metadata.WorkflowState = WorkflowState.InProgress;

        await serviceTrain.EffectRunner.Update(serviceTrain.Metadata);

        return Unit.Default;
    }

    /// <summary>
    /// Updates the train metadata to reflect the final state of the execution.
    /// </summary>
    internal static async Task<Unit> FinishServiceTrain<TIn, TOut>(
        this ServiceTrain<TIn, TOut> serviceTrain,
        Either<Exception, TOut> result
    )
    {
        serviceTrain.EffectRunner.AssertLoaded();
        serviceTrain.Metadata.AssertLoaded();

        var failureReason = result.IsRight ? null : result.Swap().ValueUnsafe();

        var resultState = result.IsRight
            ? WorkflowState.Completed
            : failureReason is OperationCanceledException
                ? WorkflowState.Cancelled
                : WorkflowState.Failed;
        serviceTrain.Logger?.LogTrace(
            "Setting ({TrainName}) to ({ResultState}).",
            serviceTrain.TrainName,
            resultState.ToString()
        );
        serviceTrain.Metadata.WorkflowState = resultState;
        serviceTrain.Metadata.EndTime = DateTime.UtcNow;
        serviceTrain.Metadata.CurrentlyRunningStep = null;
        serviceTrain.Metadata.StepStartedAt = null;

        if (failureReason != null)
            serviceTrain.Metadata.AddException(failureReason);

        await serviceTrain.EffectRunner.Update(serviceTrain.Metadata);

        return Unit.Default;
    }
}
