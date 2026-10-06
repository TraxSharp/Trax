using System.Text.Json;
using HotChocolate.Subscriptions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Samples.ChatService.Subscriptions;
using Trax.Samples.ChatService.Trains.CreateChatRoom;
using Trax.Samples.ChatService.Trains.InviteToChatRoom;
using Trax.Samples.ChatService.Trains.JoinChatRoom;
using Trax.Samples.ChatService.Trains.SendMessage;

namespace Trax.Samples.ChatService.Hooks;

/// <summary>
/// Lifecycle hook that intercepts completed chat mutation trains and publishes
/// their output to room-scoped HotChocolate subscription topics.
///
/// When a SendMessage, CreateChatRoom, JoinChatRoom or InviteToChatRoom train completes,
/// this hook extracts the chatRoomId from the serialized output and publishes
/// a ChatSubscriptionEvent to the "ChatRoom:{chatRoomId}" topic. Any client
/// subscribed to that room receives the event in real time.
///
/// A lifecycle hook runs for every train, so the hook filters by the train's
/// canonical name (its service interface's FullName). It does not depend on
/// [TraxBroadcast], and the chat trains deliberately do not carry it: a broadcast
/// train's onTrainCompleted delivers every run's output to every caller the train
/// admits, so any user would read every room's messages. The room-scoped topic
/// here is the only way a chat event leaves the server.
/// </summary>
public class ChatLifecycleHook(ITopicEventSender eventSender) : ITrainLifecycleHook
{
    private static readonly Dictionary<string, string> TrainEventTypes = new()
    {
        [typeof(ISendMessageTrain).FullName!] = "MessageSent",
        [typeof(ICreateChatRoomTrain).FullName!] = "RoomCreated",
        [typeof(IJoinChatRoomTrain).FullName!] = "UserJoined",
        [typeof(IInviteToChatRoomTrain).FullName!] = "UserJoined",
    };

    public async Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        if (
            !TrainEventTypes.TryGetValue(metadata.Name, out var eventType)
            || metadata.Output is null
        )
            return;

        Guid chatRoomId;
        try
        {
            using var doc = JsonDocument.Parse(metadata.Output);
            if (
                !doc.RootElement.TryGetProperty("chatRoomId", out var roomIdElement)
                && !doc.RootElement.TryGetProperty("ChatRoomId", out roomIdElement)
            )
                return;

            chatRoomId = roomIdElement.GetGuid();
        }
        catch (JsonException)
        {
            return;
        }

        var chatEvent = new ChatSubscriptionEvent(
            ChatRoomId: chatRoomId,
            EventType: eventType,
            Payload: metadata.Output,
            Timestamp: metadata.EndTime ?? DateTime.UtcNow,
            TrainExternalId: metadata.ExternalId
        );

        await eventSender.SendAsync(ChatSubscriptions.Topic(chatRoomId), chatEvent, ct);
    }
}
