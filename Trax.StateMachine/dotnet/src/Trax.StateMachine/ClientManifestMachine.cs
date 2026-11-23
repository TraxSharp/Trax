using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>One exposed machine in a <see cref="ClientManifest"/>.</summary>
public sealed record ClientManifestMachine
{
    /// <summary>The machine's id (<c>topic-map</c>).</summary>
    public required string Id { get; init; }

    /// <summary>The SHA-256 of the machine's IR, which every one of its document ids carries.</summary>
    public required string IrHash { get; init; }

    /// <summary>The machine's IR, from which a twin is generated.</summary>
    public required JsonObject Ir { get; init; }

    /// <summary>
    /// The IR's text as the host exported it, canonical JSON: the bytes <see cref="IrHash"/> is the SHA-256 of, and
    /// the bytes a twin generated from it hashes again, so the twin and the documents name one version.
    /// </summary>
    public string IrText() => CanonicalJson.Serialize(Ir);

    /// <summary>Null: each of the machine's operations is held to its own declared rule.</summary>
    public ClientManifestRequirement? Requires { get; init; }

    /// <summary>The machine's persisted documents, ordered by operation.</summary>
    public IReadOnlyList<ClientManifestDocument> Documents { get; init; } = [];
}
