using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Client;

/// <summary>A document and the variables it is sent with.</summary>
/// <param name="Document">The document.</param>
/// <param name="Variables">Its variables as JSON; a document with none sends an empty object.</param>
public sealed record TraxRequest(TraxDocument Document, JsonObject Variables);
