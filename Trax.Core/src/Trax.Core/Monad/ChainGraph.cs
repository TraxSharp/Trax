using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

/// <summary>
/// A train's declared chain as an immutable graph: every step it declares, with the types that flow
/// through it, every track a routing step declares, and an id for each node that the steps of a run
/// report as they execute.
/// </summary>
/// <remarks>
/// <para>Built from <see cref="Train.Train{TInput,TReturn}.DeclaredChain"/>, so nothing runs to read it.
/// It is the shape tools draw and check; the recorder it is built from stays the input to
/// <see cref="ChainVerification"/>.</para>
/// <para>A node's id names its step rather than its position: the junction, decision or routing
/// key, numbered by how many earlier steps in the same chain or track share that name, and
/// prefixed by the routing step and track it sits in, as in
/// <c>Switch&lt;Source&gt;#0/Papers/FetchPapers#0</c>. Inserting a different step leaves every
/// other id as it was. While a run executes a junction, question or routing step,
/// <see cref="CurrentNodeId"/> holds the id of the node it is executing.</para>
/// </remarks>
/// <param name="Train">The train's full type name.</param>
/// <param name="Input">The train's input type.</param>
/// <param name="Output">The train's output type.</param>
/// <param name="Nodes">The chain's steps, in the order it declares them.</param>
/// <param name="Refusals">What the declaration did that no step can express, as <see cref="ChainRecorder.Refusals"/> lists it.</param>
public sealed record ChainGraph(
    string Train,
    string Input,
    string Output,
    IReadOnlyList<ChainGraphNode> Nodes,
    IReadOnlyList<string> Refusals
)
{
    private static readonly AsyncLocal<(string NodeId, string? BranchPath)?> Running = new();

    /// <summary>
    /// The id of the declared node whose junction, question or routing step is executing on this
    /// async flow, or null outside one.
    /// </summary>
    /// <remarks>
    /// Set by the chain for the duration of the step, so anything the step calls, such as the
    /// junction effects of an <c>EffectJunction</c> or a decision observer, can say which node it
    /// belongs to. A train run inside a junction sets its own for its own steps.
    /// </remarks>
    public static string? CurrentNodeId => Running.Value?.NodeId;

    /// <summary>
    /// The path of the <c>Parallel</c> branch the step executing on this async flow belongs to,
    /// as in <c>Parallel#0/cocitation</c>, or null for a step outside any branch.
    /// </summary>
    /// <remarks>
    /// Two branches can each ask the same question, so a recorded answer is told apart by the
    /// branch as well as by how many times the question was asked.
    /// </remarks>
    public static string? CurrentBranchPath => Running.Value?.BranchPath;

    internal static void Enter(string nodeId, string? branchPath) =>
        Running.Value = (nodeId, branchPath);

    /// <summary>
    /// A hash of the graph's canonical JSON (<see cref="ToJson"/>), as 64 lowercase hex digits.
    /// Two reads of an unchanged train give the same hash; a change to any step, type or track
    /// gives another.
    /// </summary>
    public string Hash =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ToJson())));

    /// <summary>
    /// Builds the graph of a chain read by <see cref="Train.Train{TInput,TReturn}.DeclaredChain"/>.
    /// </summary>
    /// <param name="chain">The recorded chain.</param>
    /// <param name="train">The train type.</param>
    /// <param name="input">The train's input type.</param>
    /// <param name="output">The train's output type.</param>
    public static ChainGraph From(ChainRecorder chain, Type train, Type input, Type output)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(train);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        return new ChainGraph(
            train.FullName ?? train.ReadableName(),
            input.ReadableName(),
            output.ReadableName(),
            NodesOf(chain, new ChainNodeScope("")),
            chain.Refusals.ToList()
        );
    }

    private static List<ChainGraphNode> NodesOf(ChainRecorder chain, ChainNodeScope scope)
    {
        var nodes = new List<ChainGraphNode>(chain.Steps.Count);

        for (var i = 0; i < chain.Steps.Count; i++)
        {
            var step = chain.Steps[i];
            var id = scope.Next(ChainNodeScope.KeyOf(step));

            var tracks = chain
                .TracksAt(i)
                .Select(track => new ChainGraphTrack(
                    track.Name,
                    track.Description,
                    track.IsFallback,
                    NodesOf(track.Steps, scope.Track(id, track.Name))
                ))
                .ToList();

            nodes.Add(
                new ChainGraphNode(
                    id,
                    step.Kind,
                    step.Junction?.ReadableName(),
                    step.In?.ReadableName(),
                    step.Out?.ReadableName(),
                    step.Kind == ChainStepKind.IChain,
                    tracks
                )
            );
        }

        return nodes;
    }

    /// <summary>
    /// The graph as canonical JSON: properties in a fixed order, no insignificant whitespace, and
    /// absent values written as null, so the same graph always gives the same text.
    /// </summary>
    public string ToJson()
    {
        using var buffer = new MemoryStream();

        // Relaxed escaping keeps generic names readable (<, >, +); the text is data, never markup.
        using (
            var json = new Utf8JsonWriter(
                buffer,
                new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }
            )
        )
        {
            json.WriteStartObject();
            json.WriteString("train", Train);
            json.WriteString("input", Input);
            json.WriteString("output", Output);
            WriteNodes(json, Nodes);
            json.WriteStartArray("refusals");
            foreach (var refusal in Refusals)
                json.WriteStringValue(refusal);
            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteNodes(Utf8JsonWriter json, IReadOnlyList<ChainGraphNode> nodes)
    {
        json.WriteStartArray("nodes");

        foreach (var node in nodes)
        {
            json.WriteStartObject();
            json.WriteString("id", node.Id);
            json.WriteString("kind", node.Kind.ToString());
            WriteOptional(json, "junction", node.Junction);
            WriteOptional(json, "in", node.In);
            WriteOptional(json, "out", node.Out);
            json.WriteBoolean("opaque", node.Opaque);
            json.WriteStartArray("tracks");

            foreach (var track in node.Tracks)
            {
                json.WriteStartObject();
                json.WriteString("name", track.Name);
                WriteOptional(json, "description", track.Description);
                json.WriteBoolean("isFallback", track.IsFallback);
                WriteNodes(json, track.Nodes);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteOptional(Utf8JsonWriter json, string name, string? value)
    {
        if (value is null)
            json.WriteNull(name);
        else
            json.WriteString(name, value);
    }
}

/// <summary>
/// One declared step of a <see cref="ChainGraph"/>.
/// </summary>
/// <param name="Id">The node's id, unique within its graph; see <see cref="ChainGraph"/>.</param>
/// <param name="Kind">Which chain primitive declared the step.</param>
/// <param name="Junction">The junction or decider type, or null for a step that names none.</param>
/// <param name="In">The type the step consumes from Memory, or null.</param>
/// <param name="Out">The type the step contributes to Memory, or null.</param>
/// <param name="Opaque">
/// True when what runs is decided only at run time, so the graph cannot say what the step does:
/// an <see cref="ChainStepKind.IChain"/> step names an interface, and the junction behind it is
/// whatever Memory or the container holds when the run gets there.
/// </param>
/// <param name="Tracks">The tracks of a routing step, in declared order; empty for any other step.</param>
public sealed record ChainGraphNode(
    string Id,
    ChainStepKind Kind,
    string? Junction,
    string? In,
    string? Out,
    bool Opaque,
    IReadOnlyList<ChainGraphTrack> Tracks
);

/// <summary>
/// One track of a routing step in a <see cref="ChainGraph"/>.
/// </summary>
/// <param name="Name">The track's name: an enum member, <c>Yes</c>, <c>No</c>, <c>Unsure</c> or <c>Otherwise</c>.</param>
/// <param name="Description">What the track is for, as offered to the decider.</param>
/// <param name="IsFallback">True for the <c>Otherwise</c> or <c>Unsure</c> track.</param>
/// <param name="Nodes">The track's steps, in declared order.</param>
public sealed record ChainGraphTrack(
    string Name,
    string? Description,
    bool IsFallback,
    IReadOnlyList<ChainGraphNode> Nodes
);

/// <summary>
/// Numbers the nodes of one chain or track, the same way when a chain is read and when it runs, so
/// a running step's id is the id of the node the graph drew for it.
/// </summary>
internal sealed class ChainNodeScope(string prefix)
{
    private readonly Dictionary<string, int> _seen = [];

    /// <summary>The id of the next node named <paramref name="key"/> in this chain or track.</summary>
    public string Next(string key)
    {
        var ordinal = _seen.GetValueOrDefault(key);
        _seen[key] = ordinal + 1;
        return $"{prefix}{key}#{ordinal}";
    }

    /// <summary>The scope of a track of the routing step <paramref name="routingId"/>.</summary>
    public ChainNodeScope Track(string routingId, string track) => new($"{routingId}/{track}/");

    /// <summary>The name a recorded step is numbered under.</summary>
    public static string KeyOf(ChainStep step) =>
        step.Kind switch
        {
            ChainStepKind.Chain or ChainStepKind.IChain or ChainStepKind.ShortCircuit =>
                JunctionKey(step.Junction),
            ChainStepKind.Decide => DecideKey(step.Out ?? step.In),
            ChainStepKind.Switch or ChainStepKind.Gate or ChainStepKind.Scale => RoutingKey(
                step.Kind,
                step.Out is { IsGenericType: true } taken
                    ? taken.GetGenericArguments()[0]
                    : step.Out
            ),
            ChainStepKind.Extract =>
                $"Extract<{step.In?.ReadableName()}, {step.Out?.ReadableName()}>",
            ChainStepKind.Seed => $"Seed<{step.Out?.ReadableName()}>",
            ChainStepKind.Resolve => "Resolve",
            _ => step.Kind.ToString(),
        };

    /// <summary>The name a junction step is numbered under: the junction type.</summary>
    public static string JunctionKey(Type? junction) => junction?.ReadableName() ?? "Junction";

    /// <summary>The name a question is numbered under: the decision it puts in Memory.</summary>
    public static string DecideKey(Type? decision) => $"Decide<{decision?.ReadableName()}>";

    /// <summary>The name a routing step is numbered under: its kind and the key it routes on.</summary>
    public static string RoutingKey(ChainStepKind kind, Type? key) =>
        $"{kind}<{key?.ReadableName()}>";
}
