using System.Text.Json;
using HotChocolate.Subscriptions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Samples.ChatService.Trains.CreateChatRoom;
using Trax.Samples.ChatService.Trains.JoinChatRoom;
using Trax.Samples.ChatService.Trains.SendMessage;

namespace Trax.Samples.ChatService.Hooks;

/// <summary>
/// Lifecycle hook that intercepts completed chat mutation trains and publishes
/// their output to room-scoped HotChocolate subscription topics.
///
/// When a SendMessage, CreateChatRoom, or JoinChatRoom train completes,
/// this hook extracts the chatRoomId from the serialized output and publishes
/// a ChatSubscriptionEvent to the "ChatRoom:{chatRoomId}" topic. Any client
/// subscribed to that room receives the event in real time.
/// </summary>
public class ChatLifecycleHook(ITopicEventSender eventSender) : ITrainLifecycleHook
{
    private static readonly Dictionary<string, string> TrainEventTypes = new()
    {
        [typeof(ISendMessageTrain).FullName!] = "MessageSent",
        [typeof(ICreateChatRoomTrain).FullName!] = "RoomCreated",
        [typeof(IJoinChatRoomTrain).FullName!] = "UserJoined",
    };

    private static readonly HashSet<string> ChatTrains = TrainEventTypes.Keys.ToHashSet();

    public async Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        if (!ChatTrains.Contains(metadata.Name) || metadata.Output is null)
            return;

        var eventType = TrainEventTypes[metadata.Name];

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

        var chatEvent = new Subscriptions.ChatSubscriptionEvent(
            ChatRoomId: chatRoomId,
            EventType: eventType,
            Payload: metadata.Output,
            Timestamp: metadata.EndTime ?? DateTime.UtcNow,
            TrainExternalId: metadata.ExternalId
        );

        await eventSender.SendAsync($"ChatRoom:{chatRoomId}", chatEvent, ct);
    }
}
