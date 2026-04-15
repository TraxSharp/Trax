namespace Trax.Samples.GameServer.Auth;

/// <summary>
/// Demo API keys used by the GameServer sample. For demonstration only: these
/// are plaintext constants published in the Trax repository. Any production
/// system that ships them is broken. See
/// <c>Trax.Api/SECURITY-DISCLAIMER.md</c>.
/// </summary>
public static class ApiKeyDefaults
{
    /// <summary>Admin user key. Grants roles <c>Admin</c> and <c>Player</c>.</summary>
    public const string AdminKey = "admin-key-do-not-use-in-production";

    /// <summary>Player user key. Grants role <c>Player</c>.</summary>
    public const string PlayerKey = "player-key-do-not-use-in-production";
}
