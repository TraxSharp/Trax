using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

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
)
{
    /// <summary>True when <paramref name="other"/> is the same track with equal nodes.</summary>
    public bool Equals(ChainGraphTrack? other) =>
        other is not null
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(Description, other.Description, StringComparison.Ordinal)
        && IsFallback == other.IsFallback
        && Nodes.SequenceEqual(other.Nodes);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, Description, IsFallback);
}
