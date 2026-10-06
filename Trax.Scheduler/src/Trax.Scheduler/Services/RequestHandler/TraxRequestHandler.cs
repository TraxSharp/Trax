using System.Text.Json;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Mediator.Services.TrustedExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.RunExecutor;
using Trax.Scheduler.Trains.JobRunner;
using Trax.Scheduler.Utilities;

namespace Trax.Scheduler.Services.RequestHandler;

/// <summary>
/// Default implementation of <see cref="ITraxRequestHandler"/>.
/// </summary>
internal class TraxRequestHandler(
    IJobRunnerTrain jobRunnerTrain,
    ITrainExecutionService executionService,
    ITrustedExecutionScope trustedScope,
    ITrainRegistry trainRegistry,
    ITrainDiscoveryService trainDiscovery,
    ILogger<TraxRequestHandler> logger
) : ITraxRequestHandler
{
    /// <summary>
    /// What a runner reports for a failure that is not a <see cref="TrainException"/>. The
    /// exception itself is in this process's log; its message and stack are not sent back.
    /// </summary>
    internal const string UnreportedFailureMessage =
        "The runner could not complete the request; its log has the detail.";

    public async Task<ExecuteJobResult> ExecuteJobAsync(
        RemoteJobRequest request,
        CancellationToken ct = default
    )
    {
        object? deserializedInput = null;
        if (request.Input is not null && request.InputType is not null)
        {
            var type = ResolveRegisteredInputType(request.InputType);
            deserializedInput = JsonSerializer.Deserialize(
                request.Input,
                type,
                TraxJsonSerializationOptions.ManifestProperties
            );
        }

        var jobRequest = deserializedInput is not null
            ? new RunJobRequest(request.MetadataId, deserializedInput)
            : new RunJobRequest(request.MetadataId);

        await jobRunnerTrain.Run(jobRequest, ct);

        return new ExecuteJobResult(request.MetadataId);
    }

    /// <summary>
    /// Finds the input type among the registered trains' input types. The name comes from the
    /// request, so it is only ever compared, never loaded.
    /// </summary>
    private Type ResolveRegisteredInputType(string inputTypeName) =>
        RegisteredInputTypes.Find(trainRegistry, inputTypeName)
        ?? throw new TrainException(
            "The request's input type is not the input of any registered train."
        );

    public async Task<RemoteRunResponse> RunTrainAsync(
        RemoteRunRequest request,
        CancellationToken ct = default
    )
    {
        try
        {
            RefuseSchedulerTrain(request.TrainName);

            // Remote job submissions were already authorized at the original API
            // submission point. Mark this execution as trusted so the mediator's
            // authorization service skips the per-train check.
            using var _ = trustedScope.BeginTrusted("scheduler.remote-run");
            var result = await executionService.RunAsync(request.TrainName, request.InputJson, ct);

            string? outputJson = null;
            string? outputType = null;

            if (result.Output is not null)
            {
                outputType = result.Output.GetType().FullName;
                outputJson = JsonSerializer.Serialize(
                    result.Output,
                    result.Output.GetType(),
                    TraxJsonSerializationOptions.ManifestProperties
                );
            }

            return new RemoteRunResponse(
                result.MetadataId,
                result.ExternalId,
                outputJson,
                outputType
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Run execution failed for train {TrainName}", request.TrainName);

            return BuildErrorResponse(ex);
        }
    }

    /// <summary>
    /// Refuses a name that is, or could resolve to, one of the scheduler's own trains
    /// (<see cref="AdminTrains"/>). The run path runs the host's trains; the ManifestManager, the
    /// JobDispatcher, the JobRunner and the cleanup trains are started by the scheduler in its own
    /// process. The execution service accepts a full name or a short one, so both are checked: a
    /// scheduler train's full name is refused outright, and a short name unless the host registers
    /// a train of its own under it.
    /// </summary>
    private void RefuseSchedulerTrain(string trainName)
    {
        if (!IsSchedulerTrainName(trainName))
            return;

        throw new TrainException(
            $"'{trainName}' is one of the scheduler's own trains, which a runner does not run."
        );
    }

    private bool IsSchedulerTrainName(string trainName)
    {
        if (AdminTrains.FullNames.Contains(trainName, StringComparer.Ordinal))
            return true;

        var registrations = trainDiscovery.DiscoverTrains();

        foreach (var registration in registrations)
            if (
                string.Equals(
                    registration.ServiceType.FullName,
                    trainName,
                    StringComparison.Ordinal
                ) && AdminTrains.Includes(registration)
            )
                return true;

        if (!AdminTrains.ShortNames.Contains(trainName, StringComparer.Ordinal))
            return false;

        foreach (var registration in registrations)
            if (
                string.Equals(registration.ServiceTypeName, trainName, StringComparison.Ordinal)
                && !AdminTrains.Includes(registration)
            )
                return false;

        return true;
    }

    /// <summary>
    /// Builds a <see cref="RemoteRunResponse"/> with structured error fields from an exception.
    /// Uses the <see cref="TrainExceptionData"/> attached to the exception when there is one; else,
    /// for a <see cref="TrainException"/> whose message is a serialized
    /// <see cref="TrainExceptionData"/>, the fields in that message. Otherwise reports the
    /// exception's type, and its message only when it is a <see cref="TrainException"/>. No stack
    /// trace leaves the runner: the train's metadata row and this process's log hold it.
    /// </summary>
    /// <remarks>
    /// Only a <see cref="TrainException"/> is rebuilt from a recorded failure, so only its message
    /// is read as one; any other exception's message is its own text. A failure class outside
    /// <see cref="FailureClass"/> is carried as <see cref="FailureClass.Unclassified"/>, as
    /// <see cref="RunExecutor.RemoteRunJson"/> reads one on the wire.
    /// </remarks>
    internal static RemoteRunResponse BuildErrorResponse(Exception ex)
    {
        // Priority 1: structured data on the exception object. A train rethrows the original
        // exception with its junction context — and its failure classification — attached here, so
        // this is the path a locally-run train actually takes. The message is only JSON when the
        // failure already crossed a boundary once.
        if (ex.Data["TrainExceptionData"] is TrainExceptionData attached)
        {
            return new RemoteRunResponse(
                MetadataId: 0,
                IsError: true,
                ErrorMessage: attached.Message,
                ExceptionType: attached.Type,
                FailureJunction: attached.Junction,
                FailureClass: Defined(attached.FailureClass)
            )
            {
                PublicMessage = AuthorsMessage(attached.Type, attached.Message),
            };
        }

        // Priority 2: JSON-serialized data in a TrainException's message (already crossed a
        // boundary). No other exception type is rebuilt from a recorded failure.
        if (ex is TrainException && ex.Message.StartsWith('{'))
        {
            try
            {
                var data = JsonSerializer.Deserialize<TrainExceptionData>(ex.Message);

                if (data is not null)
                {
                    return new RemoteRunResponse(
                        MetadataId: 0,
                        IsError: true,
                        ErrorMessage: data.Message,
                        ExceptionType: data.Type,
                        FailureJunction: data.Junction,
                        FailureClass: Defined(data.FailureClass)
                    )
                    {
                        PublicMessage = AuthorsMessage(data.Type, data.Message),
                    };
                }
            }
            catch (JsonException)
            {
                // Not a TrainExceptionData JSON: fall through to plain extraction.
            }
        }

        return new RemoteRunResponse(
            MetadataId: 0,
            IsError: true,
            ErrorMessage: ex is TrainException ? ex.Message : UnreportedFailureMessage,
            ExceptionType: ex.GetType().Name
        )
        {
            PublicMessage = AuthorsMessage(ex.GetType().Name, ex.Message),
        };
    }

    /// <summary>
    /// The message a client may see: the message of a plain <see cref="TrainException"/>, which a
    /// train author wrote for the caller, and nothing for any other type, a type derived from it
    /// included. A message that is itself a carried failure's JSON is not an author's message.
    /// See <c>Trax.Docs/adr/0028-a-remote-runs-client-message-is-chosen-by-the-runner.md</c>.
    /// </summary>
    private static string? AuthorsMessage(string? exceptionType, string? message) =>
        string.Equals(exceptionType, nameof(TrainException), StringComparison.Ordinal)
        && !string.IsNullOrEmpty(message)
        && !message.AsSpan().TrimStart().StartsWith("{")
            ? message
            : null;

    private static FailureClass? Defined(FailureClass? failureClass) =>
        failureClass is { } value && !Enum.IsDefined(value)
            ? FailureClass.Unclassified
            : failureClass;
}
