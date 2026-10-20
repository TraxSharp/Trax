using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trax.Effect.StateMachine;

/// <summary>
/// What a host exports for <c>trax client</c>: every persisted document the host generates, by id, each exposed
/// machine's IR, each exposed train's and query model's field and arguments, what each requires of its caller, and
/// the GraphQL types those documents, trains and query models reach. A host writes it from the documents it registers, so its ids are the ones it serves; the CLI reads
/// only this file, so a frontend is generated with no running server and no credential. It never lists an operation
/// under <c>operations</c>.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/client-manifest">SDK reference</seealso>
public sealed record ClientManifest
{
    /// <summary>
    /// The format this version writes and reads. A reader refuses any other, so a manifest and the tool that reads it
    /// agree on its shape; it moves when a field is added, removed or changes meaning. Format 2 writes an output
    /// schema several outcomes share once, under <c>$defs</c>. Format 3 lists each train's arguments in place of a
    /// generated document, since a frontend writes the documents that reach a train, and lists the query models and
    /// the filter and sort inputs.
    /// </summary>
    public const int CurrentFormat = 3;

    /// <summary>The format the file was written in.</summary>
    public int Format { get; init; } = CurrentFormat;

    /// <summary>The exposed machines, ordered by id.</summary>
    public IReadOnlyList<ClientManifestMachine> Machines { get; init; } = [];

    /// <summary>The exposed trains, ordered by their field.</summary>
    public IReadOnlyList<ClientManifestTrain> Trains { get; init; } = [];

    /// <summary>The query models, each a field under <c>discover</c>, ordered by namespace and field.</summary>
    public IReadOnlyList<ClientManifestQueryModel> QueryModels { get; init; } = [];

    /// <summary>
    /// The names of the types in <see cref="Types"/> that are filter or sort inputs (a query model's <c>where</c> and
    /// <c>order</c>, and every input they are built from). A client document writes them in its text: a variable of
    /// one, or of an input holding one, is refused, by the CLI and by the host.
    /// </summary>
    public IReadOnlyList<string> FilterInputs { get; init; } = [];

    /// <summary>
    /// The documents that follow a run of a train whose <see cref="ClientManifestTrain.Progress"/> is set: the
    /// <c>query</c> that reads it once and the <c>subscription</c> that streams it. Empty when no train sets it.
    /// </summary>
    public IReadOnlyList<ClientManifestDocument> Progress { get; init; } = [];

    /// <summary>
    /// The GraphQL type definitions the documents reach, and every type an exposed train's or query model's arguments
    /// and answer reach, as SDL, so a generator checks and types every document a frontend writes. Only those types: the rest of the
    /// schema stays on the server.
    /// </summary>
    public string Types { get; init; } = "";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Where a shared output schema is written, and how an outcome points at it.
    private const string Defs = "$defs";
    private const string Ref = "$ref";
    private const string RefPrefix = "#/$defs/";

    // Reading refuses a null where the type holds none, so a field a host never writes null is never read as one.
    private static readonly JsonSerializerOptions ReadJson = new(Json)
    {
        RespectNullableAnnotations = true,
    };

    /// <summary>
    /// The manifest as its file holds it: compact JSON and a final newline, the same bytes for the same content, so a
    /// committed copy can be compared. An output schema two or more outcomes share, across every machine, is written
    /// once under <c>$defs</c> and each outcome points at it, so a machine whose invoked trains answer with one type
    /// does not repeat it per state. <see cref="Read"/> puts it back, so each machine's IR, and its hash, is the one
    /// the host exported.
    /// </summary>
    public static string Write(ClientManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var root = JsonSerializer.SerializeToNode(manifest, Json)!.AsObject();
        ShareOutputSchemas(root);
        return root.ToJsonString(Json) + "\n";
    }

    // Every outcome's output schema, across every machine's IR, with the outcome object holding it.
    private static IEnumerable<JsonObject> OutcomesWithOutput(JsonObject root) =>
        (root["machines"] as JsonArray ?? [])
            .Select(m => m?["ir"]?["outcomes"] as JsonObject)
            .OfType<JsonObject>()
            .SelectMany(outcomes => outcomes.Select(o => o.Value as JsonObject))
            .OfType<JsonObject>()
            .Where(o => o["output"] is JsonObject);

    private static void ShareOutputSchemas(JsonObject root)
    {
        var shared = OutcomesWithOutput(root)
            .GroupBy(o => CanonicalJson.Serialize(o["output"]!.AsObject()))
            .Where(g => g.Count() > 1)
            .Select(g =>
                (
                    Name: "output-"
                        + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(g.Key)))[
                            ..16
                        ],
                    Outcomes: g.ToList()
                )
            )
            .OrderBy(g => g.Name, StringComparer.Ordinal)
            .ToList();
        if (shared.Count == 0)
            return;

        var defs = new JsonObject();
        foreach (var (name, outcomes) in shared)
        {
            defs[name] = outcomes[0]["output"]!.DeepClone();
            foreach (var outcome in outcomes)
                outcome["output"] = new JsonObject { [Ref] = RefPrefix + name };
        }
        root[Defs] = defs;
    }

    // Puts each shared output schema back where Write took it from. A pointer at nothing is malformed.
    private static void RestoreOutputSchemas(JsonObject root)
    {
        if (root[Defs] is not JsonObject defs)
            return;
        root.Remove(Defs);

        foreach (var outcome in OutcomesWithOutput(root))
        {
            if (outcome["output"]![Ref] is not JsonValue pointer)
                continue;
            var target =
                pointer.TryGetValue<string>(out var text) && text.StartsWith(RefPrefix)
                    ? defs[text[RefPrefix.Length..]]
                    : null;
            outcome["output"] =
                target?.DeepClone()
                ?? throw Malformed(
                    "$.machines",
                    $"An outcome points at {pointer.ToJsonString()}, which $defs does not hold."
                );
        }
    }

    /// <summary>
    /// Reads a manifest. Throws <see cref="InvalidOperationException"/> naming both formats when the file was written
    /// in another format than <see cref="CurrentFormat"/>, and saying what is wrong when the text is not a manifest at
    /// all, or a field is missing, null or of another type.
    /// </summary>
    public static ClientManifest Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        JsonObject root;
        try
        {
            root =
                JsonNode.Parse(text) as JsonObject
                ?? throw new InvalidOperationException("The file is not a client manifest.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The file is not a client manifest.", ex);
        }

        var format = root["format"] is JsonValue value && value.TryGetValue<int>(out var f) ? f : 0;
        if (format != CurrentFormat)
            throw new InvalidOperationException(
                $"The client manifest is in format {format}, and this version reads format {CurrentFormat}. "
                    + (
                        format > CurrentFormat
                            ? "Update the trax tool to the version of the host that exported it."
                            : "Export the manifest again from a host of this version."
                    )
            );

        RestoreOutputSchemas(root);

        ClientManifest manifest;
        try
        {
            manifest =
                root.Deserialize<ClientManifest>(ReadJson)
                ?? throw new InvalidOperationException("The file is not a client manifest.");
        }
        catch (JsonException ex)
        {
            throw Malformed(ex.Path, ex.Message, ex);
        }
        catch (InvalidOperationException ex)
        {
            // A value of the wrong kind where a JsonObject is expected (the IR) is refused this way.
            throw Malformed(null, ex.Message, ex);
        }

        // A null in a list is not a nullable annotation's to refuse: it is found here.
        if (manifest.Machines.Any(m => m is null || m.Documents.Any(d => d is null)))
            throw Malformed("$.machines", "A machine or one of its documents is null.");
        if (manifest.Trains.Any(t => t is null || t.Arguments.Any(a => a is null)))
            throw Malformed("$.trains", "A train or one of its arguments is null.");
        if (manifest.QueryModels.Any(q => q is null || q.Arguments.Any(a => a is null)))
            throw Malformed("$.queryModels", "A query model or one of its arguments is null.");
        if (manifest.FilterInputs.Any(f => f is null))
            throw Malformed("$.filterInputs", "A filter input's name is null.");
        if (manifest.Progress.Any(d => d is null))
            throw Malformed("$.progress", "A progress document is null.");
        var requirements = manifest
            .Machines.Select(m => m.Requires)
            .Concat(manifest.Trains.Select(t => t.Requires))
            .Concat(manifest.QueryModels.Select(q => q.Requires))
            .OfType<ClientManifestRequirement>();
        if (
            requirements.Any(r =>
                r.Policies.Any(p => p is null)
                || r.RoleSets.Any(set => set is null || set.Any(role => role is null))
            )
        )
            throw Malformed(null, "A role set, role or policy is null.");
        return manifest;
    }

    private static InvalidOperationException Malformed(
        string? path,
        string reason,
        Exception? inner = null
    ) =>
        new(
            "The client manifest is malformed"
                + (string.IsNullOrEmpty(path) ? "" : $" at {path}")
                + $": {reason.Trim()} Export it again from the host.",
            inner
        );
}

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

/// <summary>One persisted document a client sends by its id.</summary>
public sealed record ClientManifestDocument
{
    /// <summary>The id a client sends in place of the document.</summary>
    public required string Id { get; init; }

    /// <summary>The operation: a machine's field (<c>start</c>, an event, <c>get</c>, <c>changed</c>) or runProgress's.</summary>
    public required string Operation { get; init; }

    /// <summary>The document's text, which the host runs for the id.</summary>
    public required string Document { get; init; }
}

/// <summary>What a caller needs to reach an operation, as its declaration says. The server enforces it either way.</summary>
public sealed record ClientManifestRequirement
{
    /// <summary>Whether anyone may call it, signed in or not (<c>[TraxAllowAnonymous]</c>).</summary>
    public bool Anonymous { get; init; }

    /// <summary>
    /// The role sets, one per attribute that names roles: the caller holds at least one role of every set. Empty when
    /// no role is required.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> RoleSets { get; init; } = [];

    /// <summary>Policies the caller must satisfy, each of them.</summary>
    public IReadOnlyList<string> Policies { get; init; } = [];
}
