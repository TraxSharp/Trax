using System.Security.Cryptography;
using System.Text;

namespace Trax.Effect.Models;

/// <summary>
/// Who started a run, as the run's rows record it: the lowercase hex SHA-256 of a tag and the starter's id, never the id
/// itself. A caller who started the run directly is <c>p\0</c> and the qualified principal id (<c>{scheme}:{id}</c>,
/// Trax.Api ADR 0023); the owner of a machine instance that invoked it is <c>u\0</c> and the instance's user key. The
/// tag keeps a user key that happens to read like someone's principal id from matching their runs. Only the starter
/// may follow the run's progress
/// (Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md).
/// </summary>
internal static class RunStarter
{
    /// <summary>The hash of a caller's qualified <paramref name="principalId"/>, or null for no principal.</summary>
    public static string? OfPrincipal(string? principalId) => Hash("p\0", principalId);

    /// <summary>The hash of a machine instance owner's <paramref name="userKey"/>, or null for no user.</summary>
    public static string? OfUserKey(string? userKey) => Hash("u\0", userKey);

    private static string? Hash(string tag, string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tag + id)));
}
