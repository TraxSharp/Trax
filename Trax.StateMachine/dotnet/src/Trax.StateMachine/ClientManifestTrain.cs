using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>One exposed train in a <see cref="ClientManifest"/>.</summary>
public sealed record ClientManifestTrain
{
    /// <summary>The train's field as the schema serves it (<c>startRun</c>).</summary>
    public required string Field { get; init; }

    /// <summary><c>mutation</c> or <c>query</c>: the root the field is under.</summary>
    public required string Root { get; init; }

    /// <summary>The namespace the field sits under (<c>dispatch</c>, <c>discover</c>, or the train's own), or null.</summary>
    public string? Namespace { get; init; }

    /// <summary>
    /// The type the train's field answers with, as SDL writes it (<c>StartRunResponse!</c>). The types it names are in
    /// <see cref="ClientManifest.Types"/>; the type holding the field is not, since it holds every other train too.
    /// </summary>
    public string? Answer { get; init; }

    /// <summary>What a caller needs to run the train, as its attributes declare it.</summary>
    public ClientManifestRequirement? Requires { get; init; }

    /// <summary>The arguments the train's field takes, in the order the schema declares them.</summary>
    public IReadOnlyList<ClientManifestArgument> Arguments { get; init; } = [];

    /// <summary>
    /// Whether the caller who starts a run of the train may follow it, through the documents in
    /// <see cref="ClientManifest.Progress"/>.
    /// </summary>
    public bool Progress { get; init; }
}
