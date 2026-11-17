using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>One argument of a field in a <see cref="ClientManifest"/>.</summary>
public sealed record ClientManifestArgument
{
    /// <summary>The argument's name (<c>input</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Its type as SDL writes it (<c>StartRunInput!</c>); the types it names are in <see cref="ClientManifest.Types"/>.</summary>
    public required string Type { get; init; }

    /// <summary>Its default value as GraphQL writes it, or null when it has none.</summary>
    public string? Default { get; init; }
}
