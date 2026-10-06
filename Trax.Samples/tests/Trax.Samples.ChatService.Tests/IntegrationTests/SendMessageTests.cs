using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Exceptions;
using Trax.Samples.ChatService.Tests.Fixtures;
using Trax.Samples.ChatService.Trains.SendMessage;
using Trax.Samples.ChatService.Trains.SendMessage.Junctions;

namespace Trax.Samples.ChatService.Tests.IntegrationTests;

[TestFixture]
public class SendMessageTests
{
    [Test]
    public async Task ValidateSender_CallerIsParticipant_ReturnsInput()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice);
        var input = new SendMessageInput { ChatRoomId = roomId, Content = "Hello!" };

        var result = await new ValidateSenderJunction(db, ChatUsers.Alice).Run(input);

        result.Should().Be(input);
    }

    [Test]
    public async Task ValidateSender_CallerNotParticipant_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice);
        var junction = new ValidateSenderJunction(db, ChatUsers.Bob);

        var act = () => junction.Run(new SendMessageInput { ChatRoomId = roomId, Content = "Hi" });

        await act.Should().ThrowAsync<TrainException>().WithMessage("*not a participant*");
    }

    [Test]
    public async Task ValidateSender_EmptyContent_Throws()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice);
        var junction = new ValidateSenderJunction(db, ChatUsers.Alice);

        var act = () => junction.Run(new SendMessageInput { ChatRoomId = roomId, Content = "" });

        await act.Should().ThrowAsync<TrainException>().WithMessage("*empty*");
    }

    [Test]
    public async Task PersistMessage_StoresTheMessageAsTheCallers()
    {
        using var db = ChatDbContextFixture.Create();
        var roomId = await ChatUsers.SeedRoomAsync(db, ChatUsers.Alice);
        var junction = new PersistMessageJunction(
            db,
            ChatUsers.Alice,
            NullLogger<PersistMessageJunction>.Instance
        );

        var result = await junction.Run(
            new SendMessageInput { ChatRoomId = roomId, Content = "Test message" }
        );

        result.MessageId.Should().NotBeEmpty();
        result.ChatRoomId.Should().Be(roomId);
        result.SenderUserId.Should().Be("TraxApiKey:alice");
        result.SenderDisplayName.Should().Be("Alice");
        result.Content.Should().Be("Test message");
        db.ChatMessages.Should().ContainSingle(m => m.Id == result.MessageId);
    }
}
