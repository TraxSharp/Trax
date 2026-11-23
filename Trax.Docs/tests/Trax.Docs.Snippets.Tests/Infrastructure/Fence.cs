using System.Globalization;

namespace Trax.Docs.Snippets.Tests.Infrastructure;

/// <summary>
/// A fenced code block in a markdown page, with where it sits and what its info string asks for.
/// </summary>
/// <param name="Page">The page, relative to the docs root, with forward slashes.</param>
/// <param name="OpenLine">The 1-based line of the opening fence.</param>
/// <param name="Language">The first word of the info string, empty when there is none.</param>
/// <param name="Groups">
/// The compilations this fence joins. Empty when the fence is not marked <c>compile</c>. A bare
/// <c>compile</c> puts the fence in a compilation of its own.
/// </param>
/// <param name="Code">The fence's content, with the fence's own indentation removed.</param>
public sealed record Fence(
    string Page,
    int OpenLine,
    string Language,
    IReadOnlyList<string> Groups,
    string Code
)
{
    /// <summary>Whether the info string carries <c>fragment</c>.</summary>
    public bool IsFragment { get; init; }

    /// <summary>
    /// The file a <c>from=</c> marker names, relative to the repository root (the folder holding
    /// Trax.Docs). Null when the fence carries no <c>from=</c>.
    /// </summary>
    public string? From { get; init; }

    /// <summary>The region a <c>from=path#region</c> marker names. Null for the whole file.</summary>
    public string? FromRegion { get; init; }

    /// <summary>
    /// The file a <c>file=</c> word names: the path of the file the fence shows, in the reader's
    /// project. A page that follows another compiles with that page's fences, and a fence here
    /// replaces the one there that names the same file. Null when the fence names none.
    /// </summary>
    public string? File { get; init; }

    /// <summary>
    /// The text of the last level 1 or level 2 heading above the fence, or null when there is
    /// none. A level 3 heading does not change it, so a fence under <c>### Runtime</c> inside
    /// <c>## Signatures</c> is still a signature fence.
    /// </summary>
    public string? Section { get; init; }

    /// <summary>The 1-based line of the fence's first line of code.</summary>
    public int FirstCodeLine => OpenLine + 1;

    public bool IsCompiled => Groups.Count > 0;

    /// <summary>
    /// The markers the fence carries: <c>compile</c>, <c>fragment</c> and <c>from=</c>, each
    /// at most once however many groups it names.
    /// </summary>
    public IReadOnlyList<string> Markers
    {
        get
        {
            var markers = new List<string>();
            if (IsCompiled)
                markers.Add(Fences.Marker);
            if (IsFragment)
                markers.Add(Fences.FragmentMarker);
            if (From is not null)
                markers.Add(Fences.FromMarker);
            return markers;
        }
    }

    public bool IsCSharp => Language is "csharp" or "cs" or "c#";

    /// <summary>Whether the fence sits under a <c>## Signature</c> or <c>## Signatures</c> heading.</summary>
    public bool IsUnderSignature => Section is "Signature" or "Signatures";

    public string Location => string.Create(CultureInfo.InvariantCulture, $"{Page}:{OpenLine}");
}
