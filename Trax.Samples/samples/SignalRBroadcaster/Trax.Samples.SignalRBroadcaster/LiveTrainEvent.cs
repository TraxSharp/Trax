using Trax.Core.Exceptions;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Samples.SignalRBroadcaster;

/// <summary>
/// What the hub sends each client for one lifecycle event: the default fields plus a failure
/// reason a client may read.
/// </summary>
/// <remarks>
/// The default projection (<c>TraxClientEvent</c>) sends no failure reason at all. The reason is the
/// failing code's exception message, which can name a host, a user or a credential, and the hub sends
/// every train's events to every client it admits. This projection sends the message only when the
/// run failed with a <see cref="TrainException"/>, the type a train author throws for a message meant
/// to be read, and a fixed sentence otherwise. GraphQL subscriptions apply the same rule.
/// </remarks>
public sealed record LiveTrainEvent(
    string ExternalId,
    string TrainName,
    string EventType,
    DateTime Timestamp,
    string? FailureReason
)
{
    public const string MaskedReason = "The run failed. The reason is in the server log.";

    public static LiveTrainEvent From(TrainLifecycleEventMessage message) =>
        new(
            message.ExternalId,
            ShortName(message.TrainName),
            message.EventType,
            message.Timestamp,
            message.EventType == "Failed" ? ClientReason(message) : null
        );

    private static string ClientReason(TrainLifecycleEventMessage message) =>
        message.FailureException == nameof(TrainException) && message.FailureReason is not null
            ? message.FailureReason
            : MaskedReason;

    private static string ShortName(string trainName) =>
        trainName[(trainName.LastIndexOf('.') + 1)..];
}
