namespace Trax.Samples.GameServer.Auth;

/// <summary>
/// Authorization roles recognized by the GameServer sample. Centralizing these
/// as an enum keeps role string names in one place: <c>nameof(GameRole.Admin)</c>
/// produces the same literal that <c>[TraxAuthorize("Admin")]</c> and
/// <c>RequireRole("Admin")</c> expect.
/// </summary>
public enum GameRole
{
    /// <summary>Grants access to administrative trains (e.g. <c>BanPlayerTrain</c>).</summary>
    Admin,

    /// <summary>Grants access to regular gameplay trains.</summary>
    Player,
}
