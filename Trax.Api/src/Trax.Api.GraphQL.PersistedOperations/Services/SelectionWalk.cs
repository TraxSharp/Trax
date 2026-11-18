using System.Security.Cryptography;
using System.Text;
using HotChocolate;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Types.Pagination;

namespace Trax.Api.GraphQL.PersistedOperations.Services;

/// <summary>
/// Where a walk through an answer is: the object types above the field being selected, the fields taken to reach it,
/// and every field left out so far because it leads back to one of those types.
/// </summary>
/// <param name="owner">What the document is generated for, as a refusal names it (<c>The train 'X'</c>).</param>
internal sealed class SelectionWalk(string owner)
{
    private readonly List<string> _types = [];
    private readonly List<string> _trail = [];
    private readonly List<string> _leftOut = [];

    /// <summary>What the document is generated for, as a refusal names it.</summary>
    public string Owner { get; } = owner;

    /// <summary>Every field left out, as the path that reaches it, in the order the walk met them.</summary>
    public IReadOnlyList<string> LeftOut => _leftOut;

    /// <summary>Whether <paramref name="typeName"/> is one of the object types the walk is inside.</summary>
    public bool IsOnPath(string typeName) => _types.Contains(typeName, StringComparer.Ordinal);

    public void Enter(string typeName) => _types.Add(typeName);

    public void Exit() => _types.RemoveAt(_types.Count - 1);

    public void Take(string segment) => _trail.Add(segment);

    public void Return() => _trail.RemoveAt(_trail.Count - 1);

    /// <summary>Records <paramref name="segment"/>, reached by the fields taken so far, as left out.</summary>
    public void Leave(string segment) => _leftOut.Add(string.Join(" > ", _trail.Append(segment)));
}
