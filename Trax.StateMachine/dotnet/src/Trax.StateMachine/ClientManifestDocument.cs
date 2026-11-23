using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>One persisted document a client sends by its id.</summary>
public sealed record ClientManifestDocument
{
    /// <summary>The id a client sends in place of the document.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// The operation: a machine's field (<c>start</c>, an event, <c>get</c>, <c>changed</c>), runProgress's, or a
    /// view's field.
    /// </summary>
    public required string Operation { get; init; }

    /// <summary>The document's text, which the host runs for the id.</summary>
    public required string Document { get; init; }
}
