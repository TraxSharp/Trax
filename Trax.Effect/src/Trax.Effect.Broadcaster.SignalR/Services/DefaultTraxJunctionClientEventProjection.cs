using Trax.Effect.Broadcaster.SignalR.Models;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Broadcaster.SignalR.Services;

internal static class DefaultTraxJunctionClientEventProjection
{
    /// <summary>
    /// The client payload for a junction event. Only the step's own fields are copied: the message's
    /// output and failure fields, which a junction event leaves empty anyway, never are, and neither
    /// is the decider's name. A question's answer and confidence are copied only when
    /// <paramref name="answers"/> is set, and never when the step withheld them. Without answers,
    /// a step on a decision's track, of any kind, is sent without its name or question key.
    /// </summary>
    public static TraxJunctionClientEvent Project(TrainLifecycleEventMessage message, bool answers)
    {
        var step = message.Junction!;
        var withheld = !answers || step.AnswerWithheld;

        // A step on a decision's track (a junction, a question or a routing step) names the track
        // by what it is, so its name and key are shown only where answers are.
        var nameWithheld = step.NameWithheld || (!answers && step.TrackPosition is not null);

        return new(
            MetadataId: message.MetadataId,
            ExternalId: message.ExternalId,
            TrainName: message.TrainName,
            EventType: message.EventType,
            Timestamp: message.Timestamp,
            Position: step.Position,
            Kind: step.Kind.ToString(),
            Name: nameWithheld ? JunctionEventPayload.WithheldName : step.Name,
            State: step.State.ToString(),
            StartedAt: step.StartedAt,
            EndedAt: step.EndedAt,
            DurationMs: step.DurationMs,
            FailureClass: step.FailureClass?.ToString(),
            FailureException: step.FailureException,
            QuestionKey: nameWithheld ? null : step.QuestionKey,
            Answer: withheld ? null : step.Answer,
            Confidence: withheld ? null : step.Confidence,
            Replayed: step.Replayed,
            AnswerWithheld: step.AnswerWithheld,
            NameWithheld: nameWithheld,
            TrackPosition: step.TrackPosition
        );
    }
}
