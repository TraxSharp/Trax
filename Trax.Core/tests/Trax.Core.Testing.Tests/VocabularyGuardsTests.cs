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

    private static Task<GuardResult> Run(
        TempRepo repo,
        IReadOnlyDictionary<string, string>? allowed = null
    ) =>
        VocabularyGuards.TraxVocabularyIsUsedAsync(
            Banned,
            new ArchitectureGuardOptions { RepoRootOverride = repo.Root },
            allowed
        );

    // ── The offence ─────────────────────────────────────────────────────

    [Test]
    public async Task ForeignAttribute_IsAnOffender()
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

        var result = await Run(repo);

        result.Passed.Should().BeFalse(Adr);
        result.Offenders.Should().ContainSingle().Which.Should().Contain("src/Thing.cs:5");
    }

    [Test]
    public async Task OffenderMessage_NamesTheReplacement()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "using HotChocolate.Authorization;\npublic class T { [AllowAnonymous] public string R() => \"x\"; }"
        );

        (await Run(repo))
            .Offenders.Single()
            .Should()
            .Contain("[TraxAuthorize] or [TraxAllowAnonymous]");
    }

    [Test]
    public async Task TestRootsAreScannedToo()
    {
        using var repo = new TempRepo().Write(
            "tests/ThingTests.cs",
            "using HotChocolate.Authorization;\npublic class T { [Authorize] public string R() => \"x\"; }"
        );

        (await Run(repo)).Passed.Should().BeFalse("a fixture is as much a use as production code");
    }

    [Test]
    public async Task AttributeWithArguments_IsMatched()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "using HotChocolate.Authorization;\npublic class T { [Authorize(Roles = [\"a\"])] public string R() => \"x\"; }"
        );

        (await Run(repo)).Passed.Should().BeFalse();
    }

    [Test]
    public async Task AttributeCombinedWithOthers_IsMatched()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "using HotChocolate.Authorization;\npublic class T { [Obsolete, Authorize] public string R() => \"x\"; }"
        );

        (await Run(repo))
            .Passed.Should()
            .BeFalse("an attribute after a comma is still an attribute");
    }

    [Test]
    public async Task FullyQualifiedAttribute_IsMatched()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "public class T { [HotChocolate.Authorization.Authorize] public string R() => \"x\"; }"
        );

        (await Run(repo)).Passed.Should().BeFalse();
    }

    [Test]
    public async Task EveryOffenceIsReported_NotJustTheFirst()
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

        (await Run(repo)).Offenders.Should().HaveCount(2);
    }

    // ── What is deliberately not an offence ─────────────────────────────

    /// <summary>
    /// Short attribute names collide across libraries. A file that never mentions HotChocolate
    /// cannot be using HotChocolate's attribute, and ASP.NET Core's <c>[Authorize]</c> governs a
    /// surface Trax does not own.
    /// </summary>
    [Test]
    public async Task SameNameFromAnotherLibrary_IsNotAnOffender()
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

        (await Run(repo)).Passed.Should().BeTrue("the file never mentions the banned library");
    }

    /// <summary>
    /// A library primitive Trax has no notion of is not listed, so using it is not an offence.
    /// The guard checks the list it was given, it does not go looking for third-party types.
    /// </summary>
    [Test]
    public async Task AnUnlistedLibraryPrimitive_IsNotAnOffender()
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

        (await Run(repo))
            .Passed.Should()
            .BeTrue("[ExtendObjectType] is HotChocolate's own concept");
    }

    /// <summary>
    /// The messages that tell a consumer to stop using these attributes have to name them.
    /// </summary>
    [Test]
    public async Task MentionsInCommentsAndStrings_AreNotOffenders()
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

        (await Run(repo)).Passed.Should().BeTrue("a mention is not a use");
    }

    [Test]
    public async Task AllowlistedFile_IsSkipped()
    {
        using var repo = new TempRepo().Write(
            "src/Translation.cs",
            "using HotChocolate.Authorization;\npublic class T { [Authorize] public string R() => \"x\"; }"
        );

        var result = await Run(
            repo,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/Translation.cs"] = "speaks to the library on Trax's behalf",
            }
        );

        result.Passed.Should().BeTrue();
    }

    [Test]
    public async Task AllowlistedFile_IsNotCounted_AsInspected()
    {
        using var repo = new TempRepo()
            .Write("src/Translation.cs", "using HotChocolate.Authorization;\npublic class T { }")
            .Write("src/Other.cs", "public class O { }");

        var result = await Run(
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
    public async Task Inspected_CountsEveryFileScanned()
    {
        using var repo = new TempRepo()
            .Write("src/A.cs", "public class A { }")
            .Write("tests/B.cs", "public class B { }");

        (await Run(repo)).Inspected.Should().Be(2);
    }

    [Test]
    public async Task EmptyRepo_InspectsNothing()
    {
        using var repo = new TempRepo();

        (await Run(repo)).Inspected.Should().Be(0);
    }

    [Test]
    public async Task EmptyBannedList_PassesWithoutMatching()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "using HotChocolate.Authorization;\npublic class T { [Authorize] public string R() => \"x\"; }"
        );

        (
            await VocabularyGuards.TraxVocabularyIsUsedAsync(
                [],
                new ArchitectureGuardOptions { RepoRootOverride = repo.Root }
            )
        )
            .Passed.Should()
            .BeTrue("the repo decides which concepts are Trax's, and this one listed none");
    }

    [TestCase("[Authorize, UsePaging]")]
    [TestCase("[AllowAnonymous, GraphQLName(\"read\")]")]
    [TestCase("[property: Authorize]")]
    public async Task ForeignAttribute_IsAnOffender_WhereverItSitsInTheAttributeList(
        string attributes
    )
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "using HotChocolate.Authorization;\npublic class T { "
                + attributes
                + " public string R { get; } = \"x\"; }"
        );

        (await Run(repo))
            .Passed.Should()
            .BeFalse($"{attributes} applies the library's attribute ({Adr})");
    }

    [Test]
    public async Task NullBannedList_Throws()
    {
        var act = () =>
            VocabularyGuards.TraxVocabularyIsUsedAsync(Null.Of<IReadOnlyList<ForeignVocabulary>>());

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Test]
    public void TheSynchronousForm_ReportsTheSameOffence()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "using HotChocolate.Authorization;\npublic class T { [Authorize] public string R() => \"x\"; }"
        );

        var result = VocabularyGuards.TraxVocabularyIsUsed(
            Banned,
            new ArchitectureGuardOptions { RepoRootOverride = repo.Root }
        );

        result.Inspected.Should().Be(1);
        result.Offenders.Should().ContainSingle().Which.Should().Contain("src/Thing.cs:2");
    }

    [Test]
    public async Task FailureMessage_ExplainsTheRuleAndTheEscapeHatch()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "using HotChocolate.Authorization;\npublic class T { [Authorize] public string R() => \"x\"; }"
        );

        var message = (await Run(repo)).FailureMessage;

        message.Should().Contain("Trax owns the vocabulary for its own concepts");
        message.Should().Contain("allowlist");
    }
}
