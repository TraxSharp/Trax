using System.Globalization;

namespace Trax.Api.Tests.Meta.Tests;

/// <summary>
/// A query reads the database's clock and lowers text through
/// <c>Trax.Effect.Data.Utils.DatabaseFunctions</c>, never by building <c>DateTime.UtcNow</c> or
/// <c>string.ToLower()</c> as an expression node from reflection. A node built that way reaches the
/// query provider unseen by the analyzer profile, which bans the clock and refuses a culture-bound
/// lowering when either is written as code.
/// </summary>
/// <remarks>
/// Building the node needs its member by name, so the guard looks for the lookup: the string
/// <c>"UtcNow"</c>, <c>"ToLower"</c> or <c>"ToUpper"</c>, or a <c>nameof</c> of one of those
/// members, in <c>src/</c> outside comments.
///
/// <para>Enforces <c>Trax.Docs/adr/0061-analyzer-rules-are-a-vendors-default-set-every-one-an-error.md</c>.</para>
/// </remarks>
[Property(
    "adr",
    "Trax.Docs/adr/0061-analyzer-rules-are-a-vendors-default-set-every-one-an-error.md"
)]
[TestFixture]
public partial class NoHandBuiltClockOrLoweringTests
{
    /// <summary>The files allowed to look one up, each with its reason.</summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        // A GraphQL filter handler builds its predicate as nodes by design (HotChocolate hands it
        // the member expression), and runs over a consumer's own DbContext, which need not map
        // DatabaseFunctions. string.ToLower() is the lowering every provider translates there.
        "src/Trax.Api.GraphQL/Filtering/Operations/CaseInsensitiveExpression.cs",
    };

    [Test]
    public void No_source_builds_the_clock_or_a_lowering_from_reflection()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles.CSharp("src"))
        {
            var rel = RepoRoot.Relative(file).Replace('\\', '/');
            if (Allowed.Contains(rel))
                continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal))
                    continue;
                if (MemberLookup.IsMatch(line))
                    offenders.Add(
                        string.Create(CultureInfo.InvariantCulture, $"{rel}:{i + 1}  -> {line}")
                    );
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "a query reads the database's clock with DatabaseFunctions.UtcNow() and lowers "
                    + "text with DatabaseFunctions.Lower() (Trax.Effect.Data.Utils), which every "
                    + "provider translates; a DateTime.UtcNow or string.ToLower() node built from "
                    + "reflection hides the call from the analyzer profile. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void Every_allowed_file_still_exists()
    {
        foreach (var rel in Allowed)
            File.Exists(RepoRoot.Combine(rel))
                .Should()
                .BeTrue(
                    $"{rel} is allowed to look up the clock or a lowering; drop the entry if it is gone"
                );
    }

    [GeneratedRegex(
        @"""(UtcNow|ToLower|ToUpper)""|nameof\([^)]*\.(UtcNow|ToLower|ToUpper)\s*\)",
        RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex MemberLookup { get; }
}
