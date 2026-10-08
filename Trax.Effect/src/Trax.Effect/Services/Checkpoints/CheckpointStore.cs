using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Core.Exceptions;
using Trax.Core.Monad;
using Trax.Effect.Services.JunctionEvents;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Utils;

namespace Trax.Effect.Services.Checkpoints;

/// <summary>
/// Stores the checkpoints a run reaches, refusing one a resume could not trust: a state that
/// reaches a sensitive member, a step whose data context still holds uncommitted work, a state
/// over the size cap. Each refusal fails the step, classified permanent, and stores nothing.
/// </summary>
/// <remarks>
/// See Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.
/// A host without a data provider has no rows to write to, and its runs take no checkpoint.
/// </remarks>
internal sealed class CheckpointStore(IEnumerable<ICheckpointRows> rows, CheckpointOptions options)
    : ICheckpointStore
{
    private readonly ICheckpointRows? _rows = rows.FirstOrDefault();

    public async Task Write(CheckpointTaken checkpoint, CancellationToken cancellationToken)
    {
        if (_rows is null || CheckpointRun.Current is not { } run)
            return;

        var step = $"checkpoint '{checkpoint.NodeId}'";

        // Checked again at write time, not only at startup: the startup answer covers only the
        // assemblies loaded then.
        if (TraxRedaction.ReachesSensitiveMember(checkpoint.StateType))
            throw Refused(
                checkpoint,
                $"The {step} holds '{checkpoint.StateType.FullName}', which reaches a member "
                    + "marked [TraxSensitive]. A checkpoint is stored in plain JSON, so a state "
                    + "holding a sensitive value is never stored."
            );

        // Refused at startup too; checked here because a row's node id names every track it is
        // inside, and the track of a sensitive routing step is withheld from every record.
        // The refusal names the routing step and the checkpoint, never the node id: that holds the
        // track's name.
        if (SensitiveTrack(checkpoint.NodeId) is { } routing)
            throw Refused(
                checkpoint with
                {
                    NodeId = checkpoint.NodeId[(checkpoint.NodeId.LastIndexOf('/') + 1)..],
                },
                $"The checkpoint '{checkpoint.NodeId[(checkpoint.NodeId.LastIndexOf('/') + 1)..]}' "
                    + $"is inside a track of '{routing}', whose answer is marked "
                    + "[TraxSensitive] and withheld from every record. A resume could not find its "
                    + "way back into the track, so the checkpoint is never stored. Move it after "
                    + "the routing step."
            );

        if (checkpoint.Services is { } scope && _rows.HasUncommittedWork(scope))
            throw Refused(
                checkpoint,
                $"The {step} was reached while the step's data context still held uncommitted "
                    + "changes or an open transaction. A run resumed after it would skip work that "
                    + "never committed. Commit before the checkpoint, or move it after the commit."
            );

        var state = JsonSerializer.Serialize(checkpoint.State, checkpoint.StateType);
        var size = Encoding.UTF8.GetByteCount(state);

        if (size > options.MaxStateBytes)
            throw Refused(
                checkpoint,
                $"The {step} holds '{checkpoint.StateType.FullName}' as {size} bytes of JSON; the "
                    + $"cap is {options.MaxStateBytes}. Store pointers to the data rather than the "
                    + "data itself."
            );

        await _rows
            .Insert(
                new Models.Checkpoint.Checkpoint
                {
                    MetadataId = run.MetadataId,
                    NodeId = checkpoint.NodeId,
                    BranchPath = checkpoint.BranchPath,
                    StateType = checkpoint.StateType.FullName ?? checkpoint.StateType.Name,
                    State = state,
                    Tracks = Tracks(checkpoint.Tracks),
                    ChainHash = run.ChainHash,
                    StateFingerprint = CheckpointState.Fingerprint(checkpoint.StateType),
                    CreatedAt = DateTime.UtcNow,
                },
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The routes taken before the checkpoint, minus any whose key is marked sensitive: those
    /// answers are withheld from every record, and a checkpoint keeps none of them.
    /// </summary>
    internal static string Tracks(IEnumerable<object> tracks)
    {
        var array = new JsonArray();

        foreach (var track in tracks)
        {
            var key = track.GetType().GetGenericArguments()[0];

            if (SensitiveQuestions.IsSensitive(key))
                continue;

            var type = track.GetType();
            array.Add(
                new JsonObject
                {
                    ["key"] = key.FullName,
                    ["track"] = (string?)type.GetProperty("Track")!.GetValue(track),
                    ["fallbackReason"] = (string?)
                        type.GetProperty("FallbackReason")!.GetValue(track),
                }
            );
        }

        return array.ToJsonString();
    }

    /// <summary>
    /// The routing step (<c>Switch&lt;Vault&gt;#0</c>) of a sensitive question whose track
    /// <paramref name="nodeId"/> lies inside, or null. A node inside a track has the routing
    /// step's id and the track's name before its own, so each such pair is read from the id.
    /// </summary>
    internal static string? SensitiveTrack(string nodeId)
    {
        var segments = nodeId.Split('/');

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var routing = RoutingSegment.Match(segments[i]);
            if (routing.Success && SensitiveQuestions.IsSensitive(routing.Groups["key"].Value))
                return segments[i];
        }

        return null;
    }

    private static readonly System.Text.RegularExpressions.Regex RoutingSegment = new(
        @"^(Switch|Gate|Scale)<(?<key>.+)>#\d+$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant
    );

    private static TrainException Refused(CheckpointTaken checkpoint, string reason)
    {
        var refusal = new TrainException(reason);
        refusal.Data["TrainExceptionData"] = new TrainExceptionData
        {
            TrainName = checkpoint.Train.GetType().Name,
            TrainExternalId = checkpoint.ExternalId,
            Junction = checkpoint.NodeId,
            Type = nameof(TrainException),
            Message = reason,
            FailureClass = FailureClass.Permanent,
        };
        return refusal;
    }
}

/// <summary>
/// What the store needs to know about the run on this flow: its row and its chain's hash. Set by
/// <c>ServiceTrain.Run</c> for the run and everything it awaits.
/// </summary>
internal sealed record CheckpointRun(long MetadataId, string ChainHash)
{
    private static readonly AsyncLocal<CheckpointRun?> Run = new();

    /// <summary>The run on this flow, or null outside a run that can take checkpoints.</summary>
    public static CheckpointRun? Current
    {
        get => Run.Value;
        set => Run.Value = value;
    }
}
