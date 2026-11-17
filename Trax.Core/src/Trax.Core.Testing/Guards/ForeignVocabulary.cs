using System.Globalization;
using System.Text.RegularExpressions;
using Trax.Core.Testing.Infrastructure;

namespace Trax.Core.Testing.Guards;

/// <summary>
/// One third-party attribute a repo declines to use, and what Trax offers instead.
/// </summary>
/// <param name="Library">
/// A token that must appear in the file before the attribute is considered a match, normally the
/// library's root namespace (<c>HotChocolate</c>). Short attribute names collide across
/// libraries, so a file that never mentions the library cannot be using its attribute.
/// </param>
/// <param name="Attributes">
/// Attribute names as written in source, without the <c>Attribute</c> suffix
/// (<c>Authorize</c>, <c>AllowAnonymous</c>).
/// </param>
/// <param name="Replacement">
/// What to write instead, named precisely enough to act on: <c>[TraxAuthorize] or
/// [TraxAllowAnonymous]</c>.
/// </param>
public sealed record ForeignVocabulary(
    string Library,
    IReadOnlyList<string> Attributes,
    string Replacement
);
