using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>One query model in a <see cref="ClientManifest"/>: an entity a host serves as a field under <c>discover</c>.</summary>
public sealed record ClientManifestQueryModel
{
    /// <summary>The query model's field (<c>orders</c>).</summary>
    public required string Field { get; init; }

    /// <summary>The namespace the field sits under, or null when it is directly under <c>discover</c>.</summary>
    public string? Namespace { get; init; }

    /// <summary>
    /// The field's type as SDL writes it: a connection (<c>OrdersConnection</c>) when the model is paged, a list
    /// otherwise. The types it names are in <see cref="ClientManifest.Types"/>.
    /// </summary>
    public string? Answer { get; init; }

    /// <summary>The arguments the field takes (<c>first</c>, <c>after</c>, <c>where</c>, <c>order</c>, ...).</summary>
    public IReadOnlyList<ClientManifestArgument> Arguments { get; init; } = [];

    /// <summary>What a caller needs to read it, as the entity's attributes declare it.</summary>
    public ClientManifestRequirement? Requires { get; init; }
}
