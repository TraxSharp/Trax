using System.Text.RegularExpressions;

namespace Trax.Api.GraphQL.PersistedOperations.Storage;

/// <summary>
/// Parses ids of the form <c>name.vN</c> into <c>(name, n)</c> tuples.
/// Used by the storage layer to populate the <c>operation_name</c> and
/// <c>version</c> columns alongside the literal id.
/// </summary>
internal static partial class PersistedOperationIdParser
{
    [GeneratedRegex(
        @"^(?<name>[A-Za-z_][A-Za-z0-9_]*)_v(?<version>\d+)$",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex IdRegex();

    /// <summary>
    /// Parse an id into its name and numeric version. Throws when the id
    /// does not match the convention so misuse fails loudly.
    /// </summary>
    /// <remarks>
    /// Convention is <c>name_vN</c> (underscore, not dot) because
    /// HotChocolate's <c>OperationDocumentId</c> rejects dots. The
    /// underscore separator is the closest readable alternative.
    /// </remarks>
    public static (string Name, int Version) Parse(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var match = IdRegex().Match(id);
        if (!match.Success)
            throw new FormatException(
                $"Persisted operation id '{id}' does not match the required form 'name_vN' "
                    + "(e.g. 'userProfile_v1'). Pick a stable name and bump v on breaking shape changes."
            );

        return (match.Groups["name"].Value, int.Parse(match.Groups["version"].Value));
    }

    /// <summary>
    /// True when the id matches the <c>name.vN</c> convention.
    /// </summary>
    public static bool IsValid(string id) => !string.IsNullOrEmpty(id) && IdRegex().IsMatch(id);
}
