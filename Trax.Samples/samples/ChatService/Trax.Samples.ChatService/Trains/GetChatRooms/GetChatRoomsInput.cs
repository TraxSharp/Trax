namespace Trax.Samples.ChatService.Trains.GetChatRooms;

/// <summary>
/// Lists the caller's own rooms, so the input has no fields. An empty record still gives the train
/// a unique input type, and the GraphQL field takes no <c>input</c> argument.
/// </summary>
public record GetChatRoomsInput;
