// What the examples on api/add-a-subscription-field.md take as given: the chat domain the field
// and hook carry.
using Microsoft.EntityFrameworkCore;

public record ChatEvent(Guid ChatRoomId, string Payload);

public class Participant
{
    public long Id { get; set; }

    public Guid RoomId { get; set; }

    public string UserId { get; set; } = "";
}

public class ChatDbContext(DbContextOptions<ChatDbContext> options) : DbContext(options)
{
    public DbSet<Participant> Participants => Set<Participant>();
}

public interface ISendMessageTrain;
