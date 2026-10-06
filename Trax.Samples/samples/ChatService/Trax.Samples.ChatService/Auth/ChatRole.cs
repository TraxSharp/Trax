namespace Trax.Samples.ChatService.Auth;

/// <summary>
/// Authorization roles recognized by the ChatService sample. Centralizing these
/// as an enum keeps role string names in one place: <c>nameof(ChatRole.User)</c>
/// produces the same literal that authorization attributes expect.
/// </summary>
public enum ChatRole
{
    /// <summary>Grants access to room and message trains.</summary>
    User,
}
