---
layout: default
title: Trax.Core.Testing
description: "Reference for Trax.Core.Testing, the base architecture-guard package: ArchitectureGuardOptions, GuardResult, file helpers and the hygiene and convention guards."
parent: Testing
grand_parent: SDK Reference
nav_order: 1
---

# Trax.Core.Testing

The base package of Trax's architecture guards: the options and result types every guard shares, the helpers that find a repository's files, and three sets of guards of its own. The other guard packages (`Trax.Effect.Data.Testing`, `Trax.Mediator.Testing`, `Trax.Api.GraphQL.Testing`) build on it. See [Architecture Guards](/docs/reference/architecture-guards) for how they fit together.

Each guard is available two ways: as a static checker that returns a `GuardResult` you assert on with any framework, and, for the hygiene and repo-convention guards, as an NUnit fixture you subclass.

## ArchitectureGuardOptions

```csharp
namespace Trax.Core.Testing;

public sealed record ArchitectureGuardOptions
{
    public string? RepoRootOverride { get; init; }
    public IReadOnlyList<string> TestScanRoots { get; init; }          // ["tests"]
    public IReadOnlyList<string> SourceScanRoots { get; init; }        // ["src"]
    public IReadOnlySet<string> NoIgnoreKnownExceptions { get; init; }
    public IReadOnlySet<string> FixedDelayKnownExceptions { get; init; }
    public string ExpectedDirectoryBuildPropsVersion { get; init; }    // "1.99.99"
    public string TraxPackagePrefix { get; init; }                     // "Trax."
    public IReadOnlySet<string> CrossRepoPackageKnownExceptions { get; init; }
}
```

| Property | Default | Description |
|----------|---------|-------------|
| `RepoRootOverride` | `null` | The root to scan instead of the detected one, mainly for testing a guard against a synthetic tree |
| `TestScanRoots` | `["tests"]` | Top-level folders holding test code, scanned by the hygiene guards |
| `SourceScanRoots` | `["src"]` | Top-level folders holding production code |
| `NoIgnoreKnownExceptions` | empty | Repo-relative files exempt from the no-`[Ignore]` guard |
| `FixedDelayKnownExceptions` | empty | Repo-relative files exempt from the no-fixed-delay guard |
| `ExpectedDirectoryBuildPropsVersion` | `"1.99.99"` | The `<Version>` the root `Directory.Build.props` must declare |
| `TraxPackagePrefix` | `"Trax."` | Package ids that count as Trax packages consumed from another repository |
| `CrossRepoPackageKnownExceptions` | empty | Repo-relative project files exempt from the cross-repo package guard |

Allowlist paths are repo-relative with forward slashes.

## GuardResult

```csharp
public sealed record GuardResult(IReadOnlyList<string> Offenders, int Inspected, string FailureMessage)
{
    public bool Passed { get; }
}
```

| Member | Description |
|--------|-------------|
| `Offenders` | One description per violation, usually `path:line (reason)` |
| `Inspected` | How many files or items the guard examined. A guard that inspected nothing checked nothing; assert it is above zero. |
| `FailureMessage` | The rule, how to fix a violation, and the offender list |
| `Passed` | `Offenders` is empty |

## Infrastructure

| Type | Members | Description |
|------|---------|-------------|
| `RepoRoot` (`Trax.Core.Testing.Infrastructure`) | `Path`, `Combine(params string[])`, `Relative(string)` | The repository root: the first directory above the test assembly that contains a `*.slnx` file. Throws `InvalidOperationException` when none is found, so a repository with only a `.sln` must set `RepoRootOverride`. |
| `SourceFiles` | `CSharp`, `Projects`, `Markdown`, `CSharpUnder`, `ProjectsUnder` | Enumerate `*.cs`, `*.csproj` and `*.md` files under the root, or under an explicit root |
| `SourceText` | `StripCommentsAndStrings(string)`, `MatchingLines(string, Regex)` | Blank out comments and string literals so a pattern matches code only; list the matching lines with their numbers |

## Hygiene guards

| Checker (`HygieneGuards`) | Flags, in `TestScanRoots` |
|---------------------------|---------------------------|
| `NoIgnoreAttribute(options)` | `[Ignore]` in any form, and `Ignore =` or `IgnoreReason =` on a `TestCase`, `TestCaseSource` or fixture attribute |
| `NoLegacyAsserts(options)` | NUnit's own asserts: `Assert.That`, `Assert.AreEqual` and the rest of the classic family, `ClassicAssert`, `CollectionAssert`, `StringAssert`. The convention is one assertion library. |
| `NoFixedDelays(options)` | `Task.Delay(` and `Thread.Sleep(`, unless the line or one of the three above it carries a comment with `determinism:`, `allowed-delay:`, `measuring-interval:` or `negative-wait:` |

`HygieneGuardFixture` (`Trax.Core.Testing.Fixtures`) runs all three as NUnit tests. Subclass it, override `Options` if the defaults do not fit, and the inherited tests run in your assembly. Each test fails first if the guard inspected nothing, then on any offender.

## Repo-convention guards

| Checker (`RepoConventionGuards`) | Checks |
|----------------------------------|--------|
| `DirectoryBuildPropsVersion(options)` | The root `Directory.Build.props` exists and its `<Version>` equals `ExpectedDirectoryBuildPropsVersion` |
| `CrossRepoPackageVersions(options)` | Every `PackageReference` whose id starts with `TraxPackagePrefix` carries no `Version` or `VersionOverride` and has a `<PackageVersion>` pin in the root `Directory.Packages.props` |

`RepoConventionGuardFixture` runs both as NUnit tests.

These encode a build convention for a codebase split across several repositories that consume each other's Trax packages: each repository's own version is a fixed local-development placeholder, and every Trax package it consumes is pinned once, centrally. The version check expects `1.99.99` by default, the placeholder Trax keeps in each folder's `Directory.Build.props`. Trax itself is one repository whose packages reference each other with `ProjectReference`, so it has no Trax pins for `CrossRepoPackageVersions` to check. A consumer repository that versions differently either sets `ExpectedDirectoryBuildPropsVersion` to its own value or does not use this fixture.

## Vocabulary guard

```csharp
public sealed record ForeignVocabulary(string Library, IReadOnlyList<string> Attributes, string Replacement);

public static class VocabularyGuards
{
    public static GuardResult TraxVocabularyIsUsed(
        IReadOnlyList<ForeignVocabulary> banned,
        ArchitectureGuardOptions? options = null,
        IReadOnlyDictionary<string, string>? allowed = null
    );
}
```

Reports every use of a listed third-party attribute where Trax has its own, in both `SourceScanRoots` and `TestScanRoots`.

| Parameter | Description |
|-----------|-------------|
| `banned` | The vocabularies to refuse. `Library` is a token that must appear in a file (normally the library's root namespace) before its attributes count, unless a global or project-level `using` imports it. `Attributes` are names without the `Attribute` suffix. `Replacement` is what to write instead. |
| `options` | Scan roots and repo root; defaults apply when `null` |
| `allowed` | Repo-relative paths exempt from the scan, each mapped to the reason: the translation layer that has to speak the library's language, and the tests that write the refused attribute on purpose |

Comments and string literals are ignored.

```csharp
[Test]
public void Resolvers_use_Trax_authorization_attributes()
{
    var result = VocabularyGuards.TraxVocabularyIsUsed(
        [new ForeignVocabulary("HotChocolate", ["Authorize", "AllowAnonymous"],
            "[TraxAuthorize] or [TraxAllowAnonymous]")]);

    result.Inspected.Should().BeGreaterThan(0);
    result.Offenders.Should().BeEmpty(result.FailureMessage);
}
```

## Package

```
dotnet add package Trax.Core.Testing
```

It references NUnit, for the fixtures.
