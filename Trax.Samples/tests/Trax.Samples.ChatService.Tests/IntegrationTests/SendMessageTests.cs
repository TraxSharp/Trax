using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Samples.ChatService.Data.Entities;
using Trax.Samples.ChatService.Tests.Fixtures;
using Trax.Samples.ChatService.Trains.SendMessage;
using Trax.Samples.ChatService.Trains.SendMessage.Steps;

namespace Trax.Samples.ChatService.Tests.IntegrationTests;

[TestFixture]
public class SendMessageTests
{
    #region ValidateSenderStep

    [Test]
    public async Task ValidateSender_UserIsParticipant_ReturnsInput()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoomWithParticipant(db, "alice", "Alice");

        var step = new ValidateSenderStep(db, NullLogger<ValidateSenderStep>.Instance);
        var input = new SendMessageInput
        {
            ChatRoomId = roomId,
            SenderUserId = "alice",
            Content = "Hello!",
        };

        var result = await step.Run(input);

        result.Should().Be(input);
    }

    [Test]
    public void ValidateSender_UserNotParticipant_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var step = new ValidateSenderStep(db, NullLogger<ValidateSenderStep>.Instance);
        var input = new SendMessageInput
        {
            ChatRoomId = Guid.NewGuid(),
            SenderUserId = "unknown",
            Content = "Hello!",
        };

        var act = () => step.Run(input);

        act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not a participant*");
    }

    [Test]
    public void ValidateSender_EmptyContent_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var step = new ValidateSenderStep(db, NullLogger<ValidateSenderStep>.Instance);
        var input = new SendMessageInput
        {
            ChatRoomId = Guid.NewGuid(),
            SenderUserId = "alice",
            Content = "",
        };

        var act = () => step.Run(input);

        act.Should().ThrowAsync<ArgumentException>().WithMessage("*empty*");
    }

    #endregion

    #region PersistMessageStep

    [Test]
    public async Task PersistMessage_SavesMessageAndUpdatesLastRead()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await SeedRoomWithParticipant(db, "alice", "Alice");

        var step = new PersistMessageStep(db, NullLogger<PersistMessageStep>.Instance);
        var input = new SendMessageInput
        {
            ChatRoomId = roomId,
            SenderUserId = "alice",
            Content = "Test message",
        };

        var result = await step.Run(input);

        result.MessageId.Should().NotBeEmpty();
        result.ChatRoomId.Should().Be(roomId);
        result.SenderUserId.Should().Be("alice");
        result.SenderDisplayName.Should().Be("Alice");
        result.Content.Should().Be("Test message");

        db.ChatMessages.Should().ContainSingle(m => m.Id == result.MessageId);

        var participant = db.ChatParticipants.First(p =>
            p.ChatRoomId == roomId && p.UserId == "alice"
        );
        participant.LastReadAt.Should().NotBeNull();
    }

    #endregion

    #region Helpers

    private static async Task<Guid> SeedRoomWithParticipant(
        Data.ChatDbContext db,
        string userId,
        string displayName
    )
    {
        var room = new ChatRoom
        {
            Id = Guid.NewGuid(),
            Name = "Test Room",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
        };
        db.ChatRooms.Add(room);

        db.ChatParticipants.Add(
            new ChatParticipant
            {
                Id = Guid.NewGuid(),
                ChatRoomId = room.Id,
                UserId = userId,
                DisplayName = displayName,
                JoinedAt = DateTime.UtcNow,
            }
        );

        await db.SaveChangesAsync();
        return room.Id;
    }

    #endregion
}
