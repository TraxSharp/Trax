using Trax.Effect.Attributes;

namespace Trax.Api.Tests.Meta.Tests;

/// <summary>
/// HotChocolate's <c>[Authorize]</c> and <c>[AllowAnonymous]</c> do not appear in Trax's own
/// code. Trax owns its authorization vocabulary, translates it into whatever the server needs,
/// and cannot credibly refuse a consumer's use of another framework's attributes while writing
/// them itself.
///
/// <para>Enforces <c>Trax.Docs/adr/0013-trax-owns-the-vocabulary-for-its-own-concepts.md</c>.</para>
/// </summary>
/// <remarks>
/// A local implementation for now. It moves to <c>VocabularyGuards.TraxVocabularyIsUsed</c>,
/// shipped from Trax.Core.Testing, once a release carrying that guard is pinned here.
/// <para>
/// The exception is the translation layer. <c>AuthorizeDirectives</c> and the interceptor that
/// emits directives construct HotChocolate's <c>AuthorizeDirective</c> on purpose: that is Trax
/// speaking to the server, which is the direction that is allowed. What is banned is reading a
/// consumer's HotChocolate attribute, or decorating Trax's own types with one.
/// </remarks>
[Property("adr", "Trax.Docs/adr/0013-trax-owns-the-vocabulary-for-its-own-concepts.md")]
[TestFixture]
public class NoForeignAuthorizationAttributesTests
{
    private const string Adr =
        "Trax.Docs/adr/0013-trax-owns-the-vocabulary-for-its-own-concepts.md";

    /// <summary>
    /// An <c>[Authorize]</c> or <c>[AllowAnonymous]</c> attribute in attribute position, with or
    /// without arguments, alone or combined with others.
    /// </summary>
    private static readonly Regex ForeignAttribute = new(
        @"(?:\[|,)\s*(?:HotChocolate\.Authorization\.)?(Authorize|AllowAnonymous)\s*(?:\(|\])",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Files that name the banned attributes for a reason, each of which has to justify itself.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        // The translation layer: Trax emitting the server's directive, which is the allowed
        // direction. It constructs AuthorizeDirective, it does not read a consumer attribute.
        ["src/Trax.Api.GraphQL/Configuration/AuthorizeDirectives.cs"] =
            "emits @authorize from [TraxAuthorize]",
        ["src/Trax.Api.GraphQL/Configuration/TypeExtensionExposureInterceptor.cs"] =
            "emits @authorize from [TraxAuthorize] on a resolver",
        ["src/Trax.Api.GraphQL/TypeModules/QueryModelTypeModule.cs"] =
            "emits @authorize from [TraxAuthorize] on an entity",
        // Reads the built schema's directives to re-assert the invariant; no attributes involved.
        ["src/Trax.Api.GraphQL/Startup/QueryModelAuthorizationSchemaValidator.cs"] =
            "reads directives off the built schema",
        // The tests that pin the refusal have to write the thing being refused.
        ["tests/Trax.Api.Tests/TypeExtensionExposureTests.cs"] =
            "fixtures proving the foreign attributes are refused",
        ["tests/Trax.Api.Tests/QueryModelAllowAnonymousSchemaValidatorTests.cs"] =
            "builds schemas directly to drive the schema validator",
        // Builds a raw AddGraphQLServer schema to exercise HotChocolate's own validator. Trax's
        // translation layer is not in that pipeline, so [TraxAuthorize] would emit nothing there.
        [
            "tests/Trax.Api.Tests/PersistedOperations/UnitTests/Validation/HotChocolateSchemaValidatorTests.cs"
        ] = "raw HotChocolate schema, no Trax pipeline to translate the attribute",
    };

    [Test]
    public void TraxDoesNotUseAnotherFrameworksAuthorizationAttributes()
    {
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var file in SourceFiles.CSharp("src").Concat(SourceFiles.CSharp("tests")))
        {
            var relative = RepoRoot.Relative(file);
            if (Allowed.ContainsKey(relative))
                continue;

            inspected++;
            var text = SourceText.StripCommentsAndStrings(File.ReadAllText(file));

            // Only a file that can see HotChocolate's namespace can be using its attributes.
            if (!text.Contains("HotChocolate", StringComparison.Ordinal))
                continue;

            foreach (Match match in ForeignAttribute.Matches(text))
            {
                var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{relative}:{line} ({match.Value.Trim()})");
            }
        }

        inspected.Should().BeGreaterThan(0, "a scan that inspects nothing cannot fail honestly");

        offenders
            .Should()
            .BeEmpty(
                "Trax owns its authorization vocabulary: use [TraxAuthorize] and "
                    + "[TraxAllowAnonymous], which apply to a class, an interface or a method, and "
                    + "which Trax turns into the server's directive. Banned names: "
                    + string.Join(", ", TraxAuthorization.ForeignAuthorizationAttributes)
                    + $". See {Adr}. Offenders: "
                    + string.Join(", ", offenders)
            );
    }

    /// <summary>
    /// The allowlist names files, and a stale entry silently weakens the scan.
    /// </summary>
    [Test]
    public void EveryAllowlistedFileStillExists()
    {
        foreach (var (relative, reason) in Allowed)
        {
            File.Exists(RepoRoot.Combine(relative))
                .Should()
                .BeTrue($"'{relative}' is allowlisted ({reason}) but does not exist. See {Adr}.");
        }
    }
}
