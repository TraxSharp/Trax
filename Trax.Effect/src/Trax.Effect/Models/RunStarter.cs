using System.Security.Cryptography;
using System.Text;

namespace Trax.Effect.Models;

/// <summary>
/// Who started a run, as the run's rows record it: the lowercase hex SHA-256 of the starter's qualified principal id
/// (<c>{scheme}:{id}</c>, Trax.Api ADR 0023), never the id itself. Only that caller may follow the run's progress
/// (Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md).
/// </summary>
internal static class RunStarter
{
    /// <summary>The hash of <paramref name="principalId"/>, or null for no principal.</summary>
    public static string? Hash(string? principalId) =>
        string.IsNullOrWhiteSpace(principalId)
            ? null
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(principalId)));
}
