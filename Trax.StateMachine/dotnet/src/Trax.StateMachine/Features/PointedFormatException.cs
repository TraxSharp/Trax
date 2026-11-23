using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

/// <summary>
/// A refusal of a strict document, with where it is (<see cref="At"/>'s pointer) and what is wrong, apart, so a reader
/// of a source the document was elaborated from (a lexicon) can name its own line instead of the key.
/// </summary>
internal sealed class PointedFormatException(At at, string problem)
    : FormatException($"{at.Document} {(at.Pointer.Length == 0 ? "/" : at.Pointer)}: {problem}")
{
    internal string Pointer { get; } = at.Pointer;

    internal string Problem { get; } = problem;
}
