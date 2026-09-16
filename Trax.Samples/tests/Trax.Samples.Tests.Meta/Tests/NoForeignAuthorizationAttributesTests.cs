namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// The samples declare authorization in Trax's vocabulary, never HotChocolate's.
///
/// <para>Enforces <c>Trax.Docs/adr/0013-trax-owns-the-vocabulary-for-its-own-concepts.md</c>.</para>
/// </summary>
/// <remarks>
/// This repo is where consumers copy from, so a leak here spreads further than one anywhere else.
/// <c>[TraxAuthorize]</c> and <c>[TraxAllowAnonymous]</c> apply to a class, an interface and a
/// method, and Trax turns them into the server's <c>@authorize</c> directive, so there is nothing
/// a sample can express with HotChocolate's attributes that it cannot express with Trax's.
/// <para>
/// A local implementation for now. It moves to <c>VocabularyGuards.TraxVocabularyIsUsed</c>,
/// shipped from Trax.Core.Testing, once a release carrying that guard is pinned here.
/// </para>
/// </remarks>
[Property("adr", "Trax.Docs/adr/0013-trax-owns-the-vocabulary-for-its-own-concepts.md")]
[TestFixture]
public class NoForeignAuthorizationAttributesTests
{
    private const string Adr =
        "Trax.Docs/adr/0013-trax-owns-the-vocabulary-for-its-own-concepts.md";

    /// <summary>
    /// An <c>[Authorize]</c> or <c>[AllowAnonymous]</c> in attribute position, alone or combined
    /// with others, plain or fully qualified.
    /// </summary>
    private static readonly Regex ForeignAttribute = new(
        @"(?:\[|,)\s*(?:[\w.]+\.)?(Authorize|AllowAnonymous)\s*(?:\(|\])",
        RegexOptions.Compiled
    );

    [Test]
    public void SamplesDeclareAuthorizationInTraxsVocabulary()
    {
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles.CSharp("samples", "lib", "tests"))
        {
            inspected++;

            // Short attribute names collide across libraries. A file that never mentions
            // HotChocolate cannot be using HotChocolate's attribute, and ASP.NET Core's
            // [Authorize] governs endpoints, a surface Trax does not own.
            var text = SourceText.StripCommentsAndStrings(File.ReadAllText(file));
            if (!text.Contains("HotChocolate", StringComparison.Ordinal))
                continue;

            foreach (Match match in ForeignAttribute.Matches(text))
            {
                var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{RepoRoot.Relative(file)}:{line} ({match.Value.Trim()})");
            }
        }

        inspected.Should().BeGreaterThan(0, "a scan that inspects nothing cannot fail honestly");

        offenders
            .Should()
            .BeEmpty(
                "a sample declares authorization the way Trax does: [TraxAuthorize] to gate, "
                    + "[TraxAllowAnonymous] to open, both valid on a resolver method. See "
                    + $"{Adr}. Offenders: "
                    + string.Join(", ", offenders)
            );
    }
}
