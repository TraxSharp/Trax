using NUnit.Framework.Internal;
using Trax.Core.Testing;
using Trax.Core.Testing.Fixtures;
using Trax.Core.Testing.Guards;

namespace Trax.Core.Testing.Tests;

/// <summary>
/// Shapes the guard engines used to pass without looking at: a fixture pointed at an empty
/// tree, the other classic assertion classes, a <c>VersionOverride</c>, a library imported by
/// a global using, and an attribute written with its suffix.
/// </summary>
[TestFixture]
public class GuardCoverageGapTests
{
    private static readonly ForeignVocabulary HotChocolateAuthorization = new(
        "HotChocolate",
        ["Authorize", "AllowAnonymous"],
        "[TraxAuthorize] or [TraxAllowAnonymous]"
    );

    [Test]
    public void FixtureAssertion_OverAGuardThatInspectedNothing_Fails()
    {
        using var repo = new TempRepo().Write(
            "test/FooTests.cs",
            "public class FooTests { [Ignore(\"x\")] public void A() {} }"
        );
        var result = HygieneGuards.NoIgnoreAttribute(
            new() { RepoRootOverride = repo.Root, TestScanRoots = ["tests"] }
        );

        // An isolated context, so the fixture's failed assertion is caught here rather than
        // recorded as this test's own failure.
        using (new TestExecutionContext.IsolatedContext())
        {
            var act = () => GuardAssert.CheckedAndClean(result);

            act.Should()
                .Throw<AssertionException>(
                    "the tests live under test/, not the configured tests/, so the guard checked nothing"
                )
                .WithMessage("*inspected no files*");
        }
    }

    [TestCase("ClassicAssert.AreEqual(1, 1);", "ClassicAssert")]
    [TestCase("CollectionAssert.IsEmpty(items);", "CollectionAssert")]
    [TestCase("StringAssert.Contains(\"a\", text);", "StringAssert")]
    public void NoLegacyAsserts_FlagsTheOtherClassicAssertClasses(string statement, string name)
    {
        using var repo = new TempRepo().Write(
            "tests/Sample/FooTests.cs",
            "public class FooTests { public void A() { " + statement + " } }"
        );

        var result = HygieneGuards.NoLegacyAsserts(new() { RepoRootOverride = repo.Root });

        result.Offenders.Should().ContainSingle(o => o.Contains(name));
    }

    [Test]
    public void CrossRepoPackageVersions_FlagsAVersionOverride()
    {
        using var repo = new TempRepo()
            .Write(
                "Directory.Packages.props",
                "<Project><ItemGroup><PackageVersion Include=\"Trax.Effect\" Version=\"1.41.0\" />"
                    + "</ItemGroup></Project>"
            )
            .Write(
                "src/App/App.csproj",
                "<Project><ItemGroup>"
                    + "<PackageReference Include=\"Trax.Effect\" VersionOverride=\"2.0.0\" />"
                    + "</ItemGroup></Project>"
            );

        var result = RepoConventionGuards.CrossRepoPackageVersions(
            new() { RepoRootOverride = repo.Root }
        );

        result
            .Offenders.Should()
            .ContainSingle(o => o.Contains("VersionOverride") && o.Contains("2.0.0"));
    }

    [Test]
    public void Vocabulary_ALibraryImportedByAGlobalUsing_IsSeenInEveryFile()
    {
        using var repo = new TempRepo()
            .Write("src/GlobalUsings.cs", "global using HotChocolate.Authorization;")
            .Write(
                "src/Thing.cs",
                "public class Thing { [Authorize] public string R() => \"x\"; }"
            );

        var result = VocabularyGuards.TraxVocabularyIsUsed(
            [HotChocolateAuthorization],
            new ArchitectureGuardOptions { RepoRootOverride = repo.Root }
        );

        result.Offenders.Should().ContainSingle().Which.Should().Contain("src/Thing.cs:1");
    }

    [Test]
    public void Vocabulary_ALibraryImportedByAProjectUsing_IsSeenInEveryFile()
    {
        using var repo = new TempRepo()
            .Write(
                "src/App/App.csproj",
                "<Project><ItemGroup><Using Include=\"HotChocolate.Authorization\" /></ItemGroup></Project>"
            )
            .Write(
                "src/App/Thing.cs",
                "public class Thing { [AllowAnonymous] public string R() => \"x\"; }"
            );

        var result = VocabularyGuards.TraxVocabularyIsUsed(
            [HotChocolateAuthorization],
            new ArchitectureGuardOptions { RepoRootOverride = repo.Root }
        );

        result.Offenders.Should().ContainSingle();
    }

    [Test]
    public void Vocabulary_AnAttributeWrittenWithItsSuffix_IsAnOffender()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "using HotChocolate.Authorization;\npublic class T { [AuthorizeAttribute] public string R() => \"x\"; }"
        );

        var result = VocabularyGuards.TraxVocabularyIsUsed(
            [HotChocolateAuthorization],
            new ArchitectureGuardOptions { RepoRootOverride = repo.Root }
        );

        result.Offenders.Should().ContainSingle();
    }

    [Test]
    public void Vocabulary_TwoBannedAttributesInOneList_AreBothReported()
    {
        using var repo = new TempRepo().Write(
            "src/Thing.cs",
            "using HotChocolate.Authorization;\npublic class T { [Authorize, AllowAnonymous] public string R() => \"x\"; }"
        );

        var result = VocabularyGuards.TraxVocabularyIsUsed(
            [HotChocolateAuthorization],
            new ArchitectureGuardOptions { RepoRootOverride = repo.Root }
        );

        result.Offenders.Should().HaveCount(2);
    }
}
