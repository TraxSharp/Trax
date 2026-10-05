using Microsoft.EntityFrameworkCore;
using Trax.Samples.ChatService.E2E.Fixtures;
using Trax.Samples.ChatService.E2E.Utilities;

namespace Trax.Samples.ChatService.E2E.ChatApiTests;

/// <summary>
/// A member adds someone the chat knows to a room, and the room shows up in that person's own room
/// list; nobody outside the room can add anyone, or see who could be added.
/// </summary>
[TestFixture]
public class InviteToChatRoomTests : ChatApiTestFixture
{
    private const string Bob = "TraxApiKey:bob";

    private Task<GraphQLResponse> InviteAsync(string chatRoomId, string userId, string apiKey) =>
        GraphQL.SendAsync(
            $$"""
            mutation {
                dispatch {
                    inviteToChatRoom(input: { chatRoomId: "{{chatRoomId}}", userId: "{{userId}}" }) {
                        externalId
                        output { chatRoomId userId displayName invitedByDisplayName }
                    }
                }
            }
            """,
            apiKey: apiKey
        );

    private async Task<List<string>> RoomIdsOfAsync(string apiKey)
    {
        var rooms = await GraphQL.SendAsync(
            "query { discover { getChatRooms { rooms { id } } } }",
            apiKey: apiKey
        );
        rooms.HasErrors.Should().BeFalse(rooms.FirstErrorMessage);
        return rooms
            .GetData("discover", "getChatRooms", "rooms")
            .EnumerateArray()
            .Select(r => r.GetProperty("id").GetString()!)
            .ToList();
    }

    [Test]
    public async Task A_member_invites_someone_and_the_room_appears_in_their_room_list()
    {
        var roomId = await CreateRoomAsAsync(AliceKey, "Invite Room");

        var invited = await InviteAsync(roomId, Bob, AliceKey);

        invited.HasErrors.Should().BeFalse(invited.FirstErrorMessage);
        var output = invited.GetData("dispatch", "inviteToChatRoom", "output");
        output.GetProperty("displayName").GetString().Should().Be("Bob");
        output.GetProperty("invitedByDisplayName").GetString().Should().Be("Alice");
        (await RoomIdsOfAsync(BobKey)).Should().Contain(roomId);
    }

    [Test]
    public async Task Someone_outside_the_room_cannot_invite_anyone_to_it()
    {
        var roomId = await CreateRoomAsAsync(AliceKey, "Private Room");

        var invited = await InviteAsync(roomId, Bob, CharlieKey);

        invited.HasErrors.Should().BeTrue();
        invited.FirstErrorMessage.Should().Contain("not a participant");
        (await RoomIdsOfAsync(BobKey)).Should().NotContain(roomId);
        (
            await ChatDb
                .ChatParticipants.AsNoTracking()
                .CountAsync(p => p.ChatRoomId == Guid.Parse(roomId))
        )
            .Should()
            .Be(1);
    }

    [Test]
    public async Task An_invite_for_someone_the_chat_does_not_know_is_refused()
    {
        var roomId = await CreateRoomAsAsync(AliceKey, "Invite Room");

        var invited = await InviteAsync(roomId, "TraxApiKey:mallory", AliceKey);

        invited.HasErrors.Should().BeTrue();
        invited.FirstErrorMessage.Should().Contain("no one called");
    }

    [Test]
    public async Task A_member_sees_who_they_could_invite_and_who_is_already_in()
    {
        var roomId = await CreateRoomAsAsync(AliceKey, "People Room");
        (await InviteAsync(roomId, Bob, AliceKey)).HasErrors.Should().BeFalse();

        var people = await GraphQL.SendAsync(
            $$"""
            query { discover { getChatRoomPeople(input: { chatRoomId: "{{roomId}}" }) { people { userId displayName isMember } } } }
            """,
            apiKey: AliceKey
        );

        people.HasErrors.Should().BeFalse(people.FirstErrorMessage);
        var list = people
            .GetData("discover", "getChatRoomPeople", "people")
            .EnumerateArray()
            .ToList();
        list.Select(p => p.GetProperty("displayName").GetString())
            .Should()
            .BeEquivalentTo(["Bob", "Charlie"]);
        list.Single(p => p.GetProperty("displayName").GetString() == "Bob")
            .GetProperty("isMember")
            .GetBoolean()
            .Should()
            .BeTrue();
        list.Single(p => p.GetProperty("displayName").GetString() == "Charlie")
            .GetProperty("isMember")
            .GetBoolean()
            .Should()
            .BeFalse();
    }

    [Test]
    public async Task Someone_outside_the_room_cannot_list_who_could_be_invited()
    {
        var roomId = await CreateRoomAsAsync(AliceKey, "People Room");

        var people = await GraphQL.SendAsync(
            $$"""
            query { discover { getChatRoomPeople(input: { chatRoomId: "{{roomId}}" }) { people { userId } } } }
            """,
            apiKey: CharlieKey
        );

        people.HasErrors.Should().BeTrue();
        people.FirstErrorMessage.Should().Contain("not a participant");
    }
}
