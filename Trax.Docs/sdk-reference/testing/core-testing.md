---
title: Trax.Core.Testing
description: "Reference for Trax.Core.Testing, the base architecture-guard package: ArchitectureGuardOptions, GuardResult, file helpers and the hygiene and convention guards."
type: reference
area: testing
package: Trax.Core.Testing
namespace:
  - Trax.Core.Testing
  - Trax.Core.Testing.Fixtures
  - Trax.Core.Testing.Guards
  - Trax.Core.Testing.Infrastructure
source:
  - Trax.Core/src/Trax.Core.Testing
parent: Testing
grand_parent: SDK Reference
nav_order: 1
---

# Trax.Core.Testing

The base package of Trax's architecture guards: the options and result types every guard shares, the helpers that find a repository's files, and three sets of guards of its own. The other guard packages ([Trax.Effect.Data.Testing](/docs/sdk-reference/testing/effect-data-testing), [Trax.Mediator.Testing](/docs/sdk-reference/testing/mediator-testing), [Trax.Api.GraphQL.Testing](/docs/sdk-reference/testing/graphql-testing)) build on it. Each guard is a static checker that returns a `GuardResult`; the hygiene and repo-convention guards also come as NUnit fixtures to subclass. The package references NUnit, for the fixtures.

## Signatures

```csharp fragment
namespace Trax.Core.Testing;

public sealed record ArchitectureGuardOptions
{
    public string? RepoRootOverride { get; init; }
    public IReadOnlyList<string> TestScanRoots { get; init; }
    public IReadOnlyList<string> SourceScanRoots { get; init; }
    public IReadOnlySet<string> NoIgnoreKnownExceptions { get; init; }
    public IReadOnlySet<string> FixedDelayKnownExceptions { get; init; }
    public string ExpectedDirectoryBuildPropsVersion { get; init; }
    public string TraxPackagePrefix { get; init; }
    public IReadOnlySet<string> CrossRepoPackageKnownExceptions { get; init; }
}

public sealed record GuardResult(IReadOnlyList<string> Offenders, int Inspected, string FailureMessage)
{
    public bool Passed { get; }
}
```

```csharp fragment
namespace Trax.Core.Testing.Guards;

public static class HygieneGuards
{
    public static GuardResult NoIgnoreAttribute(ArchitectureGuardOptions options);
    public static GuardResult NoLegacyAsserts(ArchitectureGuardOptions options);
    public static GuardResult NoFixedDelays(ArchitectureGuardOptions options);
    public static GuardResult NoUnsynchronisedAssertAfterStart(ArchitectureGuardOptions options);
}

public static class RepoConventionGuards
{
    public static GuardResult DirectoryBuildPropsVersion(ArchitectureGuardOptions options);
    public static GuardResult CrossRepoPackageVersions(ArchitectureGuardOptions options);
}

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

```csharp fragment
namespace Trax.Core.Testing.Fixtures;

[TestFixture]
public abstract class HygieneGuardFixture
{
    protected virtual ArchitectureGuardOptions Options { get; }

    [Test] public void Tests_do_not_use_the_Ignore_attribute();
    [Test] public void Tests_do_not_use_legacy_asserts();
    [Test] public void Tests_do_not_use_fixed_delays();
}

[TestFixture]
public abstract class RepoConventionGuardFixture
{
    protected virtual ArchitectureGuardOptions Options { get; }

    [Test] public void Directory_build_props_pins_the_expected_version();
    [Test] public void Cross_repo_package_references_are_centrally_managed();
}
```

```csharp fragment
namespace Trax.Core.Testing.Infrastructure;

public static class RepoRoot
{
    public static string Path { get; }
    public static string Combine(params string[] segments);
    public static string Relative(string absolute);
}

public static class SourceFiles
{
    public static IEnumerable<string> CSharp(params string[] subdirs);
    public static IEnumerable<string> Projects(params string[] subdirs);
    public static IEnumerable<string> Markdown(params string[] subdirs);
    public static IEnumerable<string> CSharpUnder(string root, params string[] subdirs);
    public static IEnumerable<string> ProjectsUnder(string root, params string[] subdirs);
}

public static class SourceText
{
    public static string StripCommentsAndStrings(string source);
    public static IReadOnlyList<(int LineNumber, string Line)> MatchingLines(string source, Regex pattern);
}
```

## Members

### ArchitectureGuardOptions

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

### GuardResult

| Member | Description |
|--------|-------------|
| `Offenders`| One description per violation, usually `path:line (reason)` |
| `Inspected` | How many files or items the guard examined. A guard that inspected nothing checked nothing; assert it is above zero. |
| `FailureMessage` | The rule, how to fix a violation, and the offender list |
| `Passed` | `Offenders` is empty |

### Infrastructure

| Type | Members | Description |
|------|---------|-------------|
| `RepoRoot` (`Trax.Core.Testing.Infrastructure`) | `Path`, `Combine(params string[])`, `Relative(string)` | The repository root: the first directory above the test assembly that contains a `*.slnx` file. Throws `InvalidOperationException` when none is found, so a repository with only a `.sln` must set `RepoRootOverride`. |
| `SourceFiles` | `CSharp`, `Projects`, `Markdown`, `CSharpUnder`, `ProjectsUnder` | Enumerate `*.cs`, `*.csproj` and `*.md` files under the root, or under an explicit root |
| `SourceText` | `StripCommentsAndStrings(string)`, `MatchingLines(string, Regex)` | Blank out comments and string literals so a pattern matches code only; list the matching lines with their numbers |

### Hygiene guards

| Checker (`HygieneGuards`) | Flags, in `TestScanRoots` |
|---------------------------|---------------------------|
| `NoIgnoreAttribute(options)` | `[Ignore]` in any form, and `Ignore =` or `IgnoreReason =` on a `TestCase`, `TestCaseSource` or fixture attribute |
| `NoLegacyAsserts(options)` | NUnit's own asserts: `Assert.That`, `Assert.AreEqual` and the rest of the classic family, `ClassicAssert`, `CollectionAssert`, `StringAssert`. The convention is one assertion library. |
| `NoFixedDelays(options)` | `Task.Delay(` and `Thread.Sleep(`, unless the line or one of the three above it carries a comment with `determinism:`, `allowed-delay:`, `measuring-interval:` or `negative-wait:` |
| `NoUnsynchronisedAssertAfterStart(options)` | A substitute's `Received` / `DidNotReceive` after a `StartAsync` call in the same method, with nothing between them that waits for the started work (`WaitAsync`, a `WaitUntil…`, `WaitFor…`, `Eventually…` or `…Until…Async` helper, `WhenReached`, an awaited `.Task`, `.Current` or local task, `Task.WhenAll` / `Task.WhenAny`, `StopAsync`). `BackgroundService.StartAsync` hands `ExecuteAsync` to the thread pool, so such a check races the work. A start that finishes its work before returning carries a `synchronous-start:` comment on its line or one of the three above |

`HygieneGuardFixture` (`Trax.Core.Testing.Fixtures`) runs all four as NUnit tests, `Tests_do_not_use_the_Ignore_attribute`, `Tests_do_not_use_legacy_asserts`, `Tests_do_not_use_fixed_delays` and `Tests_do_not_check_a_substitute_before_the_started_work_ran`, with the options its virtual `Options` property returns (the defaults unless a subclass overrides it). Each test fails first if the guard inspected nothing, then on any offender.

### Repo-convention guards

| Checker (`RepoConventionGuards`) | Checks |
|----------------------------------|--------|
| `DirectoryBuildPropsVersion(options)` | The root `Directory.Build.props` exists and its `<Version>` equals `ExpectedDirectoryBuildPropsVersion` |
| `CrossRepoPackageVersions(options)` | Every `PackageReference` whose id starts with `TraxPackagePrefix` carries no `Version` or `VersionOverride` and has a `<PackageVersion>` pin in the root `Directory.Packages.props` |

`RepoConventionGuardFixture` runs both as NUnit tests, `Directory_build_props_pins_the_expected_version` and `Cross_repo_package_references_are_centrally_managed`, with the same `Options` property and the same two failures.

These encode a build convention for a codebase split across several repositories that consume each other's Trax packages: each repository's own version is a fixed local-development placeholder, and every Trax package it consumes is pinned once, centrally. The version check expects `1.99.99` by default, the placeholder Trax keeps in each folder's `Directory.Build.props`. Trax itself is one repository whose packages reference each other with `ProjectReference`, so it has no Trax pins for `CrossRepoPackageVersions` to check. A repository that versions differently sets `ExpectedDirectoryBuildPropsVersion` to its own value.

### Vocabulary guard

`TraxVocabularyIsUsed` reports every use of a listed third-party attribute where Trax has its own, in both `SourceScanRoots` and `TestScanRoots`. Comments and string literals are ignored.

| Parameter | Description |
|-----------|-------------|
| `banned` | The vocabularies to refuse. `Library` is a token that must appear in a file (normally the library's root namespace) before its attributes count, unless a global or project-level `using` imports it. `Attributes` are names without the `Attribute` suffix. `Replacement` is what to write instead. |
| `options` | Scan roots and repo root; defaults apply when `null` |
| `allowed` | Repo-relative paths exempt from the scan, each mapped to the reason: the translation layer that has to speak the library's language, and the tests that write the refused attribute on purpose |

## Examples

A checker called directly, with any assertion library:

```csharp compile
using Trax.Core.Testing.Guards;

var result = VocabularyGuards.TraxVocabularyIsUsed([
    new ForeignVocabulary("HotChocolate", ["Authorize", "AllowAnonymous"], "[TraxAuthorize] or [TraxAllowAnonymous]"),
]);

if (result.Inspected == 0 || !result.Passed)
    throw new InvalidOperationException(result.FailureMessage);
```

The fixtures, subclassed in an NUnit test project; `[TestFixture]` on each subclass is what lets the runner find the inherited tests:

```csharp fragment
[TestFixture]
public sealed class HygieneGuards : HygieneGuardFixture
{
    protected override ArchitectureGuardOptions Options => new() { TestScanRoots = ["tests", "e2e"] };
}

[TestFixture]
public sealed class RepoConventionGuards : RepoConventionGuardFixture { }
```

## See also

- [Architecture Guards](/docs/reference/architecture-guards), how the guard packages fit together
- [Testing](/docs/sdk-reference/testing), the other test-support packages
