using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Trax.Core.Extensions;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Gherkin;

/// <summary>One instance a scenario talks about: a user machine's, by its owner, or a system machine's.</summary>
/// <summary>
/// An instance a scenario names: a user's by its owner, a system machine's at its fixed key by neither, and a keyed
/// system machine's by <see cref="Key"/>, its parts as a JSON array (<c>["T-12"]</c>).
/// </summary>
internal sealed record InstanceKey(string Machine, string? Owner, string? Key = null)
{
    /// <summary>The key's parts, or none for an instance that is not a keyed system machine's.</summary>
    internal IReadOnlyList<string> Parts =>
        Key is null
            ? []
            : JsonNode
                .Parse(Key)
                .NotNull()
                .AsArray()
                .Select(p => p.NotNull().GetValue<string>())
                .ToList();

    /// <summary>The key as a step says it: each part quoted, <c>"T-12"</c>.</summary>
    internal string Said => string.Join(' ', Parts.Select(p => $"\"{p}\""));
}
