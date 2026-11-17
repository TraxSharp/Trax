using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

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
)
{
    /// <summary>True when <paramref name="other"/> is the same step with equal tracks.</summary>
    public bool Equals(ChainGraphNode? other) =>
        other is not null
        && string.Equals(Id, other.Id, StringComparison.Ordinal)
        && Kind == other.Kind
        && string.Equals(Junction, other.Junction, StringComparison.Ordinal)
        && string.Equals(In, other.In, StringComparison.Ordinal)
        && string.Equals(Out, other.Out, StringComparison.Ordinal)
        && Opaque == other.Opaque
        && Tracks.SequenceEqual(other.Tracks);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Id, Kind, Junction, In, Out, Opaque);
}
