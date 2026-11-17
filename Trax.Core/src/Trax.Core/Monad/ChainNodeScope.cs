using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

/// <summary>
/// Numbers the nodes of one chain or track, the same way when a chain is read and when it runs, so
/// a running step's id is the id of the node the graph drew for it.
/// </summary>
internal sealed class ChainNodeScope(string prefix)
{
    private readonly Dictionary<string, int> _seen = [];

    /// <summary>What every id in this chain or track starts with: empty for a train's own chain.</summary>
    public string Prefix => prefix;

    /// <summary>The id <see cref="Next"/> would give <paramref name="key"/>, without counting it.</summary>
    public string Peek(string key) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}{key}#{_seen.GetValueOrDefault(key)}"
        );

    /// <summary>
    /// The ids <see cref="Next"/> would give <paramref name="keys"/> in turn, without counting
    /// them.
    /// </summary>
    public IReadOnlyList<string> PeekMany(IEnumerable<string> keys)
    {
        var counted = new Dictionary<string, int>(StringComparer.Ordinal);
        return keys.Select(key =>
            {
                var ordinal = _seen.GetValueOrDefault(key) + counted.GetValueOrDefault(key);
                counted[key] = counted.GetValueOrDefault(key) + 1;
                return string.Create(CultureInfo.InvariantCulture, $"{prefix}{key}#{ordinal}");
            })
            .ToList();
    }

    /// <summary>The id of the next node named <paramref name="key"/> in this chain or track.</summary>
    public string Next(string key)
    {
        var ordinal = _seen.GetValueOrDefault(key);
        _seen[key] = ordinal + 1;
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}{key}#{ordinal}");
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
            ChainStepKind.Checkpoint => CheckpointKey(step.In),
            _ => step.Kind.ToString(),
        };

    /// <summary>
    /// The type a junction or checkpoint step's key names, or null for any other step. Questions
    /// and routes are told apart by their question keys, which the chain already refuses to
    /// share between two types.
    /// </summary>
    public static Type? KeyType(ChainStep step) =>
        step.Kind switch
        {
            ChainStepKind.Chain or ChainStepKind.IChain or ChainStepKind.ShortCircuit =>
                step.Junction,
            ChainStepKind.Checkpoint => step.In,
            _ => null,
        };

    /// <summary>The name a junction step is numbered under: the junction type.</summary>
    public static string JunctionKey(Type? junction) => junction?.ReadableName() ?? "Junction";

    /// <summary>The name a checkpoint is numbered under: the state it stores.</summary>
    public static string CheckpointKey(Type? state) => $"Checkpoint<{state?.ReadableName()}>";

    /// <summary>The name a question is numbered under: the decision it puts in Memory.</summary>
    public static string DecideKey(Type? decision) => $"Decide<{decision?.ReadableName()}>";

    /// <summary>The name a routing step is numbered under: its kind and the key it routes on.</summary>
    public static string RoutingKey(ChainStepKind kind, Type? key) =>
        $"{kind}<{key?.ReadableName()}>";
}
