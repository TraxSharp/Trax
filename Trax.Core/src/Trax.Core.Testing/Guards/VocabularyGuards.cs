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

/// <summary>
/// Keeps a third-party vocabulary out of the places where Trax has one of its own.
/// </summary>
/// <remarks>
/// <para>
/// Trax builds on other libraries and says so. What this guards is narrower: a concept Trax
/// already owns, expressed in somebody else's words. Authorization posture is the worked example.
/// <c>[TraxAuthorize]</c> applies to a train, which never reaches a GraphQL schema, so the concept
/// is Trax's and predates the server; when the GraphQL side started requiring HotChocolate's
/// <c>[Authorize]</c> on a resolver, the same question had two answers with different combinator
/// rules, and consumer code was coupled to a dependency the attribute exists to hide.
/// </para>
/// <para>
/// The guard does not know which concepts are Trax's. That judgement belongs to the repo, which
/// passes the list. A library primitive Trax has no notion of, <c>[ExtendObjectType]</c> or
/// <c>[Parent]</c>, belongs to the library and is not listed: renaming it would buy nothing.
/// </para>
/// </remarks>
public static class VocabularyGuards
{
    /// <summary>
    /// Scans the given roots and reports every use of a listed third-party attribute.
    /// </summary>
    /// <param name="banned">The vocabularies this repo declines, and their Trax replacements.</param>
    /// <param name="options">Scan roots and the repo root. Source and test roots are both scanned.</param>
    /// <param name="allowed">
    /// Repo-relative paths exempt from the scan, each mapped to why. The translation layer belongs
    /// here: code that constructs the library's type to speak to the library is the direction that
    /// is allowed, and so are the tests that have to write the thing being refused.
    /// </param>
    public static GuardResult TraxVocabularyIsUsed(
        IReadOnlyList<ForeignVocabulary> banned,
        ArchitectureGuardOptions? options = null,
        IReadOnlyDictionary<string, string>? allowed = null
    )
    {
        ArgumentNullException.ThrowIfNull(banned);

        options ??= new ArchitectureGuardOptions();
        allowed ??= new Dictionary<string, string>(StringComparer.Ordinal);

        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var roots = options.SourceScanRoots.Concat(options.TestScanRoots).Distinct().ToArray();

        var patterns = banned.ToDictionary(
            entry => entry,
            entry => new Regex(
                // Attribute position: opening the list or following another attribute, then the
                // name, then arguments or the closing bracket.
                @"(?:\[|,)\s*(?:[\w.]+\.)?(?:"
                    + string.Join("|", entry.Attributes.Select(Regex.Escape))
                    + @")\s*(?:\(|\])",
                RegexOptions.Compiled
            )
        );

        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles.CSharpUnder(root, roots))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (allowed.ContainsKey(relative))
                continue;

            inspected++;

            // Comments and string literals talk about the banned attributes constantly, not least
            // in the messages that tell a consumer to stop using them.
            var text = SourceText.StripCommentsAndStrings(File.ReadAllText(file));

            foreach (var (entry, pattern) in patterns)
            {
                if (!text.Contains(entry.Library, StringComparison.Ordinal))
                    continue;

                foreach (Match match in pattern.Matches(text))
                {
                    var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                    offenders.Add(
                        $"{relative}:{line} ({match.Value.Trim()} from {entry.Library}; "
                            + $"use {entry.Replacement})"
                    );
                }
            }
        }

        return new GuardResult(
            offenders,
            inspected,
            "Trax owns the vocabulary for its own concepts: a surface declares the same way "
                + "wherever it lives, and Trax translates it into whatever the library underneath "
                + "needs. Replace the third-party attribute with the Trax one named beside it, or, "
                + "if this file is the translation layer that speaks to the library on Trax's "
                + "behalf, add it to the allowlist with a reason. Offenders: "
                + string.Join(", ", offenders)
        );
    }
}
