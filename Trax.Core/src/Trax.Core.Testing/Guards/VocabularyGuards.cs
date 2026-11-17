using System.Globalization;
using System.Text.RegularExpressions;
using Trax.Core.Testing.Infrastructure;

namespace Trax.Core.Testing.Guards;

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
public static partial class VocabularyGuards
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

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
    ) =>
        // Every await below is ConfigureAwait(false), so this waits on no context of its own.
        TraxVocabularyIsUsedAsync(banned, options, allowed).GetAwaiter().GetResult();

    /// <summary>
    /// Scans the given roots and reports every use of a listed third-party attribute, reading the
    /// files asynchronously.
    /// </summary>
    /// <param name="banned">The vocabularies this repo declines, and their Trax replacements.</param>
    /// <param name="options">Scan roots and the repo root. Source and test roots are both scanned.</param>
    /// <param name="allowed">
    /// Repo-relative paths exempt from the scan, each mapped to why. The translation layer belongs
    /// here: code that constructs the library's type to speak to the library is the direction that
    /// is allowed, and so are the tests that have to write the thing being refused.
    /// </param>
    /// <param name="cancellationToken">Stops the scan.</param>
    public static async Task<GuardResult> TraxVocabularyIsUsedAsync(
        IReadOnlyList<ForeignVocabulary> banned,
        ArchitectureGuardOptions? options = null,
        IReadOnlyDictionary<string, string>? allowed = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(banned);

        options ??= new ArchitectureGuardOptions();
        allowed ??= new Dictionary<string, string>(StringComparer.Ordinal);

        var root = options.RepoRootOverride ?? RepoRoot.Path;
        var roots = options
            .SourceScanRoots.Concat(options.TestScanRoots)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var patterns = banned.ToDictionary(
            entry => entry,
            entry => new Regex(
                // Attribute position: opening the list (with an optional target such as
                // property:) or following another attribute, then the name with or without its
                // Attribute suffix, then arguments, the closing bracket, or another attribute. The
                // lookahead leaves that comma for the next attribute in the list.
                @"(?:\[\s*(?:\w+\s*:\s*)?|,)\s*(?:[\w.]+\.)?(?:"
                    + string.Join('|', entry.Attributes.Select(Regex.Escape))
                    + @")(?:Attribute)?(?=\s*(?:\(|\]|,))",
                RegexOptions.Compiled,
                RegexTimeout
            )
        );

        var offenders = new List<string>();
        var inspected = 0;

        // A global using makes a library's attributes visible in files that never name it, so a
        // library imported that way counts as present everywhere.
        var globallyImported = await GloballyImported(root, roots, cancellationToken)
            .ConfigureAwait(false);

        foreach (var file in SourceFiles.CSharpUnder(root, roots))
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (allowed.ContainsKey(relative))
                continue;

            inspected++;

            // Comments and string literals talk about the banned attributes constantly, not least
            // in the messages that tell a consumer to stop using them.
            var text = SourceText.StripCommentsAndStrings(
                await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false)
            );

            foreach (var (entry, pattern) in patterns)
            {
                if (
                    !globallyImported.Contains(entry.Library)
                    && !text.Contains(entry.Library, StringComparison.Ordinal)
                )
                    continue;

                foreach (Match match in pattern.Matches(text))
                {
                    var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                    offenders.Add(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"{relative}:{line} ({match.Value.Trim()} from {entry.Library}; "
                        ) + $"use {entry.Replacement})"
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

    [GeneratedRegex(
        @"<Using\s+Include\s*=\s*""(?<name>[\w.]+)""",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 5_000
    )]
    private static partial Regex ProjectUsing { get; }

    /// <summary>
    /// Every namespace a global using brings in, from source files or a project's
    /// <c>&lt;Using Include&gt;</c>, with each of its parent namespaces: importing
    /// <c>HotChocolate.Authorization</c> is a use of <c>HotChocolate</c>.
    /// </summary>
    private static async Task<HashSet<string>> GloballyImported(
        string root,
        string[] roots,
        CancellationToken cancellationToken
    )
    {
        var names = new List<string>();

        foreach (var file in SourceFiles.CSharpUnder(root, roots))
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            foreach (Match match in GlobalUsing.Matches(text))
                names.Add(match.Groups["name"].Value);
        }

        foreach (var project in SourceFiles.ProjectsUnder(root, roots))
        {
            var text = await File.ReadAllTextAsync(project, cancellationToken)
                .ConfigureAwait(false);
            foreach (Match match in ProjectUsing.Matches(text))
                names.Add(match.Groups["name"].Value);
        }

        var imported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var parts = name.Split('.');
            for (var i = 1; i <= parts.Length; i++)
                imported.Add(string.Join('.', parts.Take(i)));
        }

        return imported;
    }

    [GeneratedRegex(
        @"^\s*global\s+using\s+(?:static\s+)?(?:\w+\s*=\s*)?(?<name>[\w.]+)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 5_000
    )]
    private static partial Regex GlobalUsing { get; }
}
