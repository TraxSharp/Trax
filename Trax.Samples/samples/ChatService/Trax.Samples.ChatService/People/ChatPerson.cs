namespace Trax.Samples.ChatService.People;

/// <summary>Someone the chat knows, by the id Trax gives their principal (<c>TraxApiKey:bob</c>).</summary>
public record ChatPerson(string UserId, string DisplayName);
