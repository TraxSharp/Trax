using Trax.Core.Testing;
using Trax.Core.Testing.Guards;

namespace Trax.Core.Testing.Tests;

/// <summary>
/// The vocabulary guard: a concept Trax owns, written in somebody else's words.
///
/// <para>Enforces <c>Trax.Docs/adr/0013-trax-owns-the-vocabulary-for-its-own-concepts.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0013-trax-owns-the-vocabulary-for-its-own-concepts.md")]
[TestFixture]
public class VocabularyGuardsTests
{
    private const string Adr =
        "Trax.Docs/adr/0013-trax-owns-the-vocabulary-for-its-own-concepts.md";

    private static readonly ForeignVocabulary HotChocolateAuthorization = new(
        "HotChocolate",
        ["Authorize", "AllowAnonymous"],
        "[TraxAuthorize] or [TraxAllowAnonymous]"
    );

    private static IReadOnlyList<ForeignVocabulary> Banned => [HotChocolateAuthorization];

    private static GuardResult Run(
        TempRepo repo,
        IReadOnlyDictionary<string, string>? allowed = null
    ) =>
        VocabularyGuards.TraxVocabularyIsUsed(
            Banned,
            new ArchitectureGuardOptions { RepoRootOverride = repo.Root },
            allowed
        );

    // ── The offence ─────────────────────────────────────────────────────

    [Test]
    public void ForeignAttribute_IsAnOffender()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            """
            using HotChocolate.Authorization;

            public class Thing
            {
                [Authorize]
                public string Read() => "x";
            }
            """
        );

        var result = Run(repo);

        result.Passed.Should().BeFalse(Adr);
        result.Offenders.Should().ContainSingle().Which.Should().Contain("src/Thing.cs:5");
    }

    [Test]
    public void OffenderMessage_NamesTheReplacement()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "using HotChocolate.Authorization;\npublic class T { [AllowAnonymous] public string R() => \"x\"; }"
        );

        Run(repo).Offenders.Single().Should().Contain("[TraxAuthorize] or [TraxAllowAnonymous]");
    }

    [Test]
    public void TestRootsAreScannedToo()
    {
        using var repo = new TempRepo().Write(
            "tests/ThingTests.cs",
            "using HotChocolate.Authorization;\npublic class T { [Authorize] public string R() => \"x\"; }"
        );

        Run(repo).Passed.Should().BeFalse("a fixture is as much a use as production code");
    }

    [Test]
    public void AttributeWithArguments_IsMatched()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "using HotChocolate.Authorization;\npublic class T { [Authorize(Roles = [\"a\"])] public string R() => \"x\"; }"
        );

        Run(repo).Passed.Should().BeFalse();
    }

    [Test]
    public void AttributeCombinedWithOthers_IsMatched()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "using HotChocolate.Authorization;\npublic class T { [Obsolete, Authorize] public string R() => \"x\"; }"
        );

        Run(repo).Passed.Should().BeFalse("an attribute after a comma is still an attribute");
    }

    [Test]
    public void FullyQualifiedAttribute_IsMatched()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "public class T { [HotChocolate.Authorization.Authorize] public string R() => \"x\"; }"
        );

        Run(repo).Passed.Should().BeFalse();
    }

    [Test]
    public void EveryOffenceIsReported_NotJustTheFirst()
    {
        using var repo = new TempRepo()
            .Write(
                "src/A.cs",
                "using HotChocolate.Authorization;\npublic class A { [Authorize] public string R() => \"x\"; }"
            )
            .Write(
                "src/B.cs",
                "using HotChocolate.Authorization;\npublic class B { [AllowAnonymous] public string R() => \"x\"; }"
            );

        Run(repo).Offenders.Should().HaveCount(2);
    }

    // ── What is deliberately not an offence ─────────────────────────────

    /// <summary>
    /// Short attribute names collide across libraries. A file that never mentions HotChocolate
    /// cannot be using HotChocolate's attribute, and ASP.NET Core's <c>[Authorize]</c> governs a
    /// surface Trax does not own.
    /// </summary>
    [Test]
    public void SameNameFromAnotherLibrary_IsNotAnOffender()
    {
        using var repo = new TempRepo().Write(
            "src/Controller.cs",
            """
            using Microsoft.AspNetCore.Authorization;

            public class Controller
            {
                [Authorize]
                public string Get() => "x";
            }
            """
        );

        Run(repo).Passed.Should().BeTrue("the file never mentions the banned library");
    }

    /// <summary>
    /// A library primitive Trax has no notion of is not listed, so using it is not an offence.
    /// The guard checks the list it was given, it does not go looking for third-party types.
    /// </summary>
    [Test]
    public void AnUnlistedLibraryPrimitive_IsNotAnOffender()
    {
        using var repo = new TempRepo().Write(
            "src/Extension.cs",
            """
            using HotChocolate.Types;

            [ExtendObjectType(typeof(Thing))]
            public class Extension
            {
                public string Extra() => "x";
            }
            """
        );

        Run(repo).Passed.Should().BeTrue("[ExtendObjectType] is HotChocolate's own concept");
    }

    /// <summary>
    /// The messages that tell a consumer to stop using these attributes have to name them.
    /// </summary>
    [Test]
    public void MentionsInCommentsAndStrings_AreNotOffenders()
    {
        using var repo = new TempRepo().Write(
            "src/Message.cs",
            """
            using HotChocolate.Authorization;

            public class Message
            {
                // Replace [Authorize] with [TraxAuthorize].
                public string Text => "declares [Authorize], which Trax does not read";
            }
            """
        );

        Run(repo).Passed.Should().BeTrue("a mention is not a use");
    }

    [Test]
    public void AllowlistedFile_IsSkipped()
    {
        using var repo = new TempRepo().Write(
            "src/Translation.cs",
            "using HotChocolate.Authorization;\npublic class T { [Authorize] public string R() => \"x\"; }"
        );

        var result = Run(
            repo,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/Translation.cs"] = "speaks to the library on Trax's behalf",
            }
        );

        result.Passed.Should().BeTrue();
    }

    [Test]
    public void AllowlistedFile_IsNotCounted_AsInspected()
    {
        using var repo = new TempRepo()
            .Write("src/Translation.cs", "using HotChocolate.Authorization;\npublic class T { }")
            .Write("src/Other.cs", "public class O { }");

        var result = Run(
            repo,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/Translation.cs"] = "translation layer",
            }
        );

        result.Inspected.Should().Be(1);
    }

    // ── The scan has to be able to fail ─────────────────────────────────

    /// <summary>
    /// A guard that inspected nothing passes vacuously. Consumers assert on Inspected for exactly
    /// this reason, so it has to be truthful.
    /// </summary>
    [Test]
    public void Inspected_CountsEveryFileScanned()
    {
        using var repo = new TempRepo()
            .Write("src/A.cs", "public class A { }")
            .Write("tests/B.cs", "public class B { }");

        Run(repo).Inspected.Should().Be(2);
    }

    [Test]
    public void EmptyRepo_InspectsNothing()
    {
        using var repo = new TempRepo();

        Run(repo).Inspected.Should().Be(0);
    }

    [Test]
    public void EmptyBannedList_PassesWithoutMatching()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "using HotChocolate.Authorization;\npublic class T { [Authorize] public string R() => \"x\"; }"
        );

        VocabularyGuards
            .TraxVocabularyIsUsed([], new ArchitectureGuardOptions { RepoRootOverride = repo.Root })
            .Passed.Should()
            .BeTrue("the repo decides which concepts are Trax's, and this one listed none");
    }

    [Test]
    public void NullBannedList_Throws()
    {
        var act = () => VocabularyGuards.TraxVocabularyIsUsed(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void FailureMessage_ExplainsTheRuleAndTheEscapeHatch()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "using HotChocolate.Authorization;\npublic class T { [Authorize] public string R() => \"x\"; }"
        );

        var message = Run(repo).FailureMessage;

        message.Should().Contain("Trax owns the vocabulary for its own concepts");
        message.Should().Contain("allowlist");
    }
}
