namespace Trax.Samples.ChatService.Trains.GetChatHistory;

public record GetChatHistoryInput
{
    /// <summary>How many messages a call returns when it does not say.</summary>
    public const int DefaultTake = 50;

    /// <summary>The most messages one call returns; a larger <see cref="Take"/> is clamped to it.</summary>
    public const int MaxTake = 100;

    public Guid ChatRoomId { get; init; }

    /// <summary>
    /// How many of the most recent messages to return; <see cref="DefaultTake"/> when omitted.
    /// Nullable so the GraphQL field is optional: an initializer on a non-nullable property does
    /// not become a GraphQL default, and the field would be required.
    /// </summary>
    public int? Take { get; init; }
    public DateTime? Before { get; init; }
}
