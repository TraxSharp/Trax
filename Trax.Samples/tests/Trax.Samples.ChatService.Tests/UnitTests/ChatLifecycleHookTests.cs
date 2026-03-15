using System.Text.Json;
using FluentAssertions;
using HotChocolate.Subscriptions;
using Moq;
using Trax.Effect.Models.Metadata;
using Trax.Samples.ChatService.Hooks;
using Trax.Samples.ChatService.Subscriptions;
using Trax.Samples.ChatService.Trains.CreateChatRoom;
using Trax.Samples.ChatService.Trains.JoinChatRoom;
using Trax.Samples.ChatService.Trains.MarkChatAsRead;
using Trax.Samples.ChatService.Trains.SendMessage;

namespace Trax.Samples.ChatService.Tests.UnitTests;

[TestFixture]
public class ChatLifecycleHookTests
{
    private Mock<ITopicEventSender> _eventSender = null!;
    private ChatLifecycleHook _hook = null!;

    [SetUp]
    public void SetUp()
    {
        _eventSender = new Mock<ITopicEventSender>();
        _hook = new ChatLifecycleHook(_eventSender.Object);
    }

    #region OnCompleted — SendMessage

    [Test]
    public async Task OnCompleted_SendMessageTrain_PublishesToCorrectTopic()
    {
        var chatRoomId = Guid.NewGuid();
        var output = new SendMessageOutput
        {
            MessageId = Guid.NewGuid(),
            ChatRoomId = chatRoomId,
            SenderUserId = "alice",
            SenderDisplayName = "Alice",
            Content = "Hello!",
            SentAt = DateTime.UtcNow,
        };

        var metadata = CreateMetadata(typeof(ISendMessageTrain), output);

        await _hook.OnCompleted(metadata, CancellationToken.None);

        _eventSender.Verify(
            s =>
                s.SendAsync(
                    $"ChatRoom:{chatRoomId}",
                    It.Is<ChatSubscriptionEvent>(e =>
                        e.ChatRoomId == chatRoomId && e.EventType == "MessageSent"
                    ),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Test]
    public async Task OnCompleted_SendMessageTrain_EventContainsSerializedPayload()
    {
        var chatRoomId = Guid.NewGuid();
        var output = new SendMessageOutput
        {
            MessageId = Guid.NewGuid(),
            ChatRoomId = chatRoomId,
            SenderUserId = "bob",
            SenderDisplayName = "Bob",
            Content = "Test message",
            SentAt = DateTime.UtcNow,
        };

        var metadata = CreateMetadata(typeof(ISendMessageTrain), output);
        ChatSubscriptionEvent? capturedEvent = null;

        _eventSender
            .Setup(s =>
                s.SendAsync(
                    It.IsAny<string>(),
                    It.IsAny<ChatSubscriptionEvent>(),
                    It.IsAny<CancellationToken>()
                )
            )
            .Callback<string, ChatSubscriptionEvent, CancellationToken>(
                (_, e, _) => capturedEvent = e
            );

        await _hook.OnCompleted(metadata, CancellationToken.None);

        capturedEvent.Should().NotBeNull();
        capturedEvent!.Payload.Should().Contain("Test message");
        capturedEvent.TrainExternalId.Should().Be(metadata.ExternalId);
    }

    #endregion

    #region OnCompleted — CreateChatRoom

    [Test]
    public async Task OnCompleted_CreateChatRoomTrain_PublishesRoomCreatedEvent()
    {
        var chatRoomId = Guid.NewGuid();
        var output = new CreateChatRoomOutput
        {
            ChatRoomId = chatRoomId,
            Name = "General",
            CreatedAt = DateTime.UtcNow,
        };

        var metadata = CreateMetadata(typeof(ICreateChatRoomTrain), output);

        await _hook.OnCompleted(metadata, CancellationToken.None);

        _eventSender.Verify(
            s =>
                s.SendAsync(
                    $"ChatRoom:{chatRoomId}",
                    It.Is<ChatSubscriptionEvent>(e => e.EventType == "RoomCreated"),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    #endregion

    #region OnCompleted — JoinChatRoom

    [Test]
    public async Task OnCompleted_JoinChatRoomTrain_PublishesUserJoinedEvent()
    {
        var chatRoomId = Guid.NewGuid();
        var output = new JoinChatRoomOutput
        {
            ChatRoomId = chatRoomId,
            UserId = "charlie",
            DisplayName = "Charlie",
            JoinedAt = DateTime.UtcNow,
        };

        var metadata = CreateMetadata(typeof(IJoinChatRoomTrain), output);

        await _hook.OnCompleted(metadata, CancellationToken.None);

        _eventSender.Verify(
            s =>
                s.SendAsync(
                    $"ChatRoom:{chatRoomId}",
                    It.Is<ChatSubscriptionEvent>(e => e.EventType == "UserJoined"),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    #endregion

    #region OnCompleted — Non-Chat Trains

    [Test]
    public async Task OnCompleted_NonChatTrain_DoesNotPublish()
    {
        var metadata = new Metadata
        {
            Name = "SomeOther.Namespace.IUnrelatedTrain",
            ExternalId = Guid.NewGuid().ToString(),
            Output = """{"someField": "value"}""",
            EndTime = DateTime.UtcNow,
        };

        await _hook.OnCompleted(metadata, CancellationToken.None);

        _eventSender.Verify(
            s =>
                s.SendAsync(
                    It.IsAny<string>(),
                    It.IsAny<ChatSubscriptionEvent>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Test]
    public async Task OnCompleted_MarkChatAsReadTrain_DoesNotPublish()
    {
        var metadata = new Metadata
        {
            Name = typeof(IMarkChatAsReadTrain).FullName!,
            ExternalId = Guid.NewGuid().ToString(),
            Output = """{"chatRoomId": "00000000-0000-0000-0000-000000000001"}""",
            EndTime = DateTime.UtcNow,
        };

        await _hook.OnCompleted(metadata, CancellationToken.None);

        _eventSender.Verify(
            s =>
                s.SendAsync(
                    It.IsAny<string>(),
                    It.IsAny<ChatSubscriptionEvent>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    #endregion

    #region OnCompleted — Edge Cases

    [Test]
    public async Task OnCompleted_NullOutput_DoesNotPublish()
    {
        var metadata = new Metadata
        {
            Name = typeof(ISendMessageTrain).FullName!,
            ExternalId = Guid.NewGuid().ToString(),
            Output = null,
            EndTime = DateTime.UtcNow,
        };

        await _hook.OnCompleted(metadata, CancellationToken.None);

        _eventSender.Verify(
            s =>
                s.SendAsync(
                    It.IsAny<string>(),
                    It.IsAny<ChatSubscriptionEvent>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Test]
    public async Task OnCompleted_MalformedJson_DoesNotPublish()
    {
        var metadata = new Metadata
        {
            Name = typeof(ISendMessageTrain).FullName!,
            ExternalId = Guid.NewGuid().ToString(),
            Output = "not valid json",
            EndTime = DateTime.UtcNow,
        };

        await _hook.OnCompleted(metadata, CancellationToken.None);

        _eventSender.Verify(
            s =>
                s.SendAsync(
                    It.IsAny<string>(),
                    It.IsAny<ChatSubscriptionEvent>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Test]
    public async Task OnCompleted_JsonWithoutChatRoomId_DoesNotPublish()
    {
        var metadata = new Metadata
        {
            Name = typeof(ISendMessageTrain).FullName!,
            ExternalId = Guid.NewGuid().ToString(),
            Output = """{"someOtherField": "value"}""",
            EndTime = DateTime.UtcNow,
        };

        await _hook.OnCompleted(metadata, CancellationToken.None);

        _eventSender.Verify(
            s =>
                s.SendAsync(
                    It.IsAny<string>(),
                    It.IsAny<ChatSubscriptionEvent>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    #endregion

    #region Helpers

    private static Metadata CreateMetadata<T>(Type trainInterface, T output)
    {
        return new Metadata
        {
            Name = trainInterface.FullName!,
            ExternalId = Guid.NewGuid().ToString(),
            Output = JsonSerializer.Serialize(output),
            EndTime = DateTime.UtcNow,
        };
    }

    #endregion
}
