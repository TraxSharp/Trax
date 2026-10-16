using System.Security.Cryptography;
using System.Text;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// The id prefix a generated train document was stored under. Trax no longer generates train documents (a frontend
/// writes the documents that reach a train, see
/// <c>Trax.Docs/adr/0054-a-frontend-writes-every-document-that-reaches-a-train.md</c>), but the prefix stays reserved:
/// no upload may use it, a row an earlier version stored under it is collected by the grace-period sweep like any
/// version no node serves, and a request for one is answered <c>SERVER_OUTDATED</c> while a node of that version may
/// still serve it and <c>CLIENT_OUTDATED</c> once it is collected, so a page built against a generated train function
/// reloads.
/// </summary>
internal static class TrainDocuments
{
    /// <summary>The id prefix train documents were stored under, which no uploaded document may use.</summary>
    public const string ReservedIdPrefix = "trax_train_";

    /// <summary>Whether <paramref name="id"/> is under the reserved prefix.</summary>
    public static bool IsReserved(string? id) =>
        id is not null && id.StartsWith(ReservedIdPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The slot a train id names, or null when the id is not one: the prefix, a root of <c>mutation</c> or
    /// <c>query</c>, a namespace segment (empty or not), a non-empty field and a well-formed hash.
    /// </summary>
    public static string? SlotOf(string id)
    {
        if (!IsReserved(id) || !MachineDocuments.IsWellFormed(id))
            return null;
        var parts = id[ReservedIdPrefix.Length..].Split('_');
        if (parts.Length != 4 || parts[0] is not ("mutation" or "query") || parts[2].Length == 0)
            return null;
        return id[..id.LastIndexOf('_')];
    }

    /// <summary>
    /// The 16 hex characters of the SHA-256 of <paramref name="shapeHash"/> and <paramref name="text"/> that end every
    /// generated id, so a stored row is a generated document only when its body is the one its id names.
    /// </summary>
    internal static string TextHash(string shapeHash, string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(shapeHash + "\n" + text)))[
            ..MachineDocuments.TextHashLength
        ];
}
