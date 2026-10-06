namespace Trax.Samples.ChatService.People;

/// <summary>Someone the chat knows, by the id Trax gives their principal (<c>TraxApiKey:bob</c>).</summary>
public record ChatPerson(string UserId, string DisplayName);

/// <summary>
/// The people a room member can invite. The host builds it from the same list it registers
/// credentials from, so an invite names a real caller and the server, not the client, supplies the
/// name the room shows.
/// </summary>
public class ChatDirectory(IEnumerable<ChatPerson> people)
{
    private readonly Dictionary<string, ChatPerson> _people = people.ToDictionary(p => p.UserId);

    public IReadOnlyCollection<ChatPerson> All => _people.Values;

    public ChatPerson? Find(string userId) => _people.GetValueOrDefault(userId);
}
