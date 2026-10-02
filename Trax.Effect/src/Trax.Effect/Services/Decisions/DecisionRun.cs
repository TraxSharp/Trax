using System.Collections.Concurrent;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Effect.Models.Metadata;

namespace Trax.Effect.Services.Decisions;

/// <summary>
/// Prepares the recording of one run's decisions before its junctions run. Implemented by
/// <c>Trax.Effect.Data</c>'s decision journal, which <c>AddDecisionRecording</c> registers.
/// </summary>
internal interface IDecisionRunRecorder
{
    /// <summary>
    /// Binds the run to its row and loads the answers it replays. Called once per run, after the
    /// row is saved and the start hooks have run, and before the first junction. Whatever it
    /// throws fails the run.
    /// </summary>
    /// <param name="metadata">The run's row.</param>
    /// <param name="train">The train's own type, which Trax.Core names its decisions after.</param>
    /// <param name="cancellationToken">The run's token.</param>
    Task<DecisionRun> Begin(Metadata metadata, Type train, CancellationToken cancellationToken);
}

/// <summary>
/// What one run needs to record and replay its decisions: the row they are written against and the
/// answers it replays.
/// </summary>
/// <remarks>
/// It lives on the run's own async flow (<see cref="Current"/>), set by <c>ServiceTrain.Run</c>, not
/// in the journal, which is a singleton. Two runs that share an external id, as a retried
/// dispatch's rows can, each see their own, and nothing outlives the run whichever way it ends.
/// </remarks>
internal sealed class DecisionRun
{
    private static readonly AsyncLocal<DecisionRun?> CurrentRun = new();

    /// <summary>The run <c>ServiceTrain.Run</c> begins, for <paramref name="train"/>'s own type.</summary>
    public DecisionRun(
        string runId,
        Type train,
        long? metadataId,
        IReadOnlyDictionary<(string Key, int Occurrence), RecordedAnswer> replay
    )
        : this(runId, NameOf(train), metadataId, replay) { }

    /// <summary>
    /// A run found by its row rather than on the async flow, for a train already named as Trax.Core
    /// names it.
    /// </summary>
    public DecisionRun(
        string runId,
        string train,
        long? metadataId,
        IReadOnlyDictionary<(string Key, int Occurrence), RecordedAnswer> replay
    )
    {
        RunId = runId;
        Train = train;
        MetadataId = metadataId;
        Replay = replay;
    }

    /// <summary>The run on this async flow, or null outside a service train's run.</summary>
    public static DecisionRun? Current
    {
        get => CurrentRun.Value;
        set => CurrentRun.Value = value;
    }

    /// <summary>The run's external id, which Trax.Core reports its decisions under.</summary>
    public string RunId { get; }

    /// <summary>The train as Trax.Core names it in <c>DecisionMade.Train</c>.</summary>
    public string Train { get; }

    /// <summary>The run's row, or null when it was never persisted and its decisions are only logged.</summary>
    public long? MetadataId { get; }

    /// <summary>
    /// The answers of the run this one repeats, each with the fingerprint it was recorded under,
    /// keyed as Trax.Core asks for them.
    /// </summary>
    public IReadOnlyDictionary<(string Key, int Occurrence), RecordedAnswer> Replay { get; }

    /// <summary>The id of the row written for each question's latest asking, for its routing.</summary>
    public ConcurrentDictionary<string, long> Latest { get; } = new();

    /// <summary>
    /// The failure for a run that names a run to replay that cannot be replayed. It is classified
    /// permanent: running it again on the same host hits the same wall, and asking afresh would
    /// break the promise the requeue made.
    /// </summary>
    public static TrainException Unreplayable(Metadata metadata, string why) =>
        Unreplayable(metadata.Name, metadata.ExternalId, metadata.ReplayDecisionsOf, why);

    /// <inheritdoc cref="Unreplayable(Metadata, string)"/>
    public static TrainException Unreplayable(
        string name,
        string externalId,
        long? replayDecisionsOf,
        string why
    )
    {
        var message =
            $"Run {externalId} of train '{name}' was queued to replay the decisions of run "
            + $"{replayDecisionsOf}, but {why}. It is failed rather than asked afresh, because a "
            + "fresh answer could take a different track from the run it repeats.";

        return Classified(new TrainException(message), name, externalId, FailureClass.Permanent);
    }

    /// <summary>
    /// The failure for a decision this run reports under an external id other than its own, which
    /// happens when the train's <c>ExternalId</c> is changed while it runs. Its decisions cannot be
    /// written against the run's row, and a decision that is not recorded is not acted on.
    /// </summary>
    public TrainException Unbound(string reportedRunId)
    {
        var message =
            $"Train '{Train}' reported a decision under the external id {reportedRunId}, but its "
            + $"run is {RunId}. The external id was changed while the run was going, so the "
            + "decision cannot be recorded against the run, and it is not acted on.";

        return Classified(new TrainException(message), Train, RunId, FailureClass.Permanent);
    }

    /// <summary>
    /// Attaches a failure class to an exception that carries none yet, so the run records it
    /// whether it fails before any junction or in a decision step.
    /// </summary>
    public static TException Classified<TException>(
        TException exception,
        string trainName,
        string runId,
        FailureClass failureClass
    )
        where TException : Exception
    {
        if (exception.Data["TrainExceptionData"] is TrainExceptionData data)
            data.FailureClass ??= failureClass;
        else
            exception.Data["TrainExceptionData"] = new TrainExceptionData
            {
                TrainName = trainName,
                TrainExternalId = runId,
                Type = exception.GetType().Name,
                Junction = "DecisionReplay",
                Message = exception.Message,
                StackTrace = exception.StackTrace,
                FailureClass = failureClass,
            };

        return exception;
    }

    /// <summary>
    /// The name Trax.Core gives a train in <c>DecisionMade.Train</c>: the type's name with its
    /// generic arguments written as C# writes them (<c>Route&lt;Order&gt;</c>), and a type nested in
    /// a generic type after its outer type. It must match Trax.Core's, which is internal there.
    /// </summary>
    public static string NameOf(Type type) => ReadableName(type);

    private static string ReadableName(Type type) =>
        ReadableName(type, type.IsGenericType ? type.GetGenericArguments() : []);

    private static string ReadableName(Type type, Type[] arguments)
    {
        var prefix = "";
        var inherited = 0;

        if (type.IsNested && type.DeclaringType is { IsGenericType: true } outer)
        {
            inherited = Math.Min(outer.GetGenericArguments().Length, arguments.Length);
            prefix = ReadableName(outer, arguments[..inherited]) + ".";
        }

        var name = type.Name;
        var tick = name.IndexOf('`');

        if (tick >= 0)
            name = name[..tick];

        var own = arguments[inherited..];

        return own.Length == 0
            ? prefix + name
            : $"{prefix}{name}<{string.Join(", ", own.Select(ReadableName))}>";
    }
}
