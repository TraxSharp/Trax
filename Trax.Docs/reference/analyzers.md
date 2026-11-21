---
title: Trax.Analyzers
description: "The opt-in Trax.Analyzers package: its Roslyn rules and bans, every one an error, and how to turn it on, pin its level and turn parts of it off."
type: reference
area: tooling
nav_order: 4
source:
  - Trax.Analyzers/src/Trax.Analyzers.Package
  - Trax.Analyzers/rules.txt
---

# Trax.Analyzers

A Roslyn analyzer package for code that builds on Trax .NET, or any other .NET code. It checks for
culture and comparer bugs, async and cancellation mistakes, regexes without a timeout, the
null-forgiving operator and about 250 other things, and it bans reading the clock, chance or the
console directly. Every rule it turns on is an error.

Nothing applies until a project references it. Trax builds all of its own code, samples and the
book's project under it ([why](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0061-analyzer-rules-are-a-vendors-default-set-every-one-an-error.md)).

| | |
|---|---|
| Package | `Trax.Analyzers`, a development dependency targeting `netstandard2.0` |
| Rule ids | `TRX0001` to `TRX0245`, plus `RS0030` for the bans |
| Rule pages | [`Trax.Analyzers/docs/rules/`](https://github.com/TraxSharp/Trax/tree/main/Trax.Analyzers/docs/rules), one per rule |
| The profile | [`Trax.Analyzers/rules.txt`](https://github.com/TraxSharp/Trax/blob/main/Trax.Analyzers/rules.txt): every rule, with its severity in source and test projects |

## Turn it on

In each project, or once in `Directory.Build.props`:

```xml
<ItemGroup>
  <PackageReference Include="Trax.Analyzers" PrivateAssets="all" />
</ItemGroup>
```

`PrivateAssets="all"` keeps it out of whatever depends on your project. A project made with
`dotnet new trax-api`, `trax-scheduler` or `trax-hub` gets the same line with `--strict`
([Project Templates](/docs/reference/templates#the-strict-option)).

The rules apply to test projects too. Expect the first build of an existing codebase to fail in
many places; `dotnet format analyzers <solution> --diagnostics TRX0004 --severity error` applies the
automatic fixes for one rule at a time. Read what it changed: a fix can change behaviour, for
example `RegexOptions.ExplicitCapture` on a regex whose groups are read by number.

## What it reports

- **The rules**, `TRX0001` and up. They are a copy of
  [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer) (MIT), renamed into Trax's
  ids, so `MA0004` is `TRX0004`. Every rule that project reports by default is an error here, and so
  are a few it leaves off, marked `added by Trax` in `rules.txt`: TRX0045, for example, reports a
  blocking call (`File.ReadAllText`, `.Result`, `Cancel()` beside `CancelAsync()`) even where the
  method has to become async to avoid it.
- **The bans**, through `RS0030` from `Microsoft.CodeAnalysis.BannedApiAnalyzers`, which the
  package brings with it:

| Group | Bans | Use instead |
|---|---|---|
| Clock | `DateTime.Now`, `UtcNow`, `Today`, the `DateTimeOffset` forms, `Stopwatch`, `Environment.TickCount` | An injected `TimeProvider`; `TimeProvider.System` where the host registers the real clock |
| Random | `Random.Shared`, `new Random()` without a seed | An injected `Random`, or a seeded one in a test |
| Console | `System.Console` | `ILogger`, or the writers a command is given |
| Process exit | `Environment.Exit`, `Environment.FailFast` | Return an exit code, or throw |
| Null-forgiving | `x!` (TRX0240), `null!` and `default!` (TRX0191) | A real value, `required`, a null test, or a method that throws when the value is null |

## Turn part of it off

Each group has an MSBuild property. Set it to `false` in the project or `Directory.Build.props`:

| Property | `false` turns off |
|---|---|
| `TraxAnalyzers` | the whole profile; the rules stay loaded at the severities they ship with, which is mostly off |
| `TraxAnalyzersTests` | the profile and the bans in test projects (`IsTestProject`) |
| `TraxBanClock` | the clock ban |
| `TraxBanRandom` | the random ban |
| `TraxBanConsole` | the console ban |
| `TraxBanProcessExit` | the process exit ban |
| `TraxBanNullForgiving` | the `!` operator; `null!` and `default!` are rule TRX0191, turned off like any rule |

One rule is turned off or down in `.editorconfig`. The profile is a global configuration at the
lowest priority, so any severity you write wins:

```ini
[*.cs]
dotnet_diagnostic.TRX0048.severity = none
```

The package also adds the nearest `BannedSyntaxes.txt` above a project to the null-forgiving ban,
so a repository can ban more syntax of its own. `TraxIncludeBannedSyntaxesFile=false` stops that.

## Pin the level

`TraxAnalysisLevel` picks the profile. A release that would add errors to an existing build ships
them under a new level, and a project gets the newest level unless it pins one:

```xml
<PropertyGroup>
  <TraxAnalysisLevel>1</TraxAnalysisLevel>
</PropertyGroup>
```

`latest`, or leaving it unset, takes the newest level the installed package has.

## Trax.Analyzers.Annotations

Attributes that tune a rule from your own code: `[RequireNamedArgument]` on a parameter whose
arguments must be named, `[CultureInsensitiveType]` to declare a type's formatting free of culture,
`[ExcludeFromBlockingCallAnalysis]` to name a method TRX0042 and TRX0045 leave alone, and the others
in the `Trax.Analyzers.Annotations` namespace. They are conditional and leave no reference in the
compiled assembly.

```xml
<PackageReference Include="Trax.Analyzers.Annotations" PrivateAssets="all" />
```

## SDK Reference

> [Packages](/docs/reference/packages#analyzers) | [Project Templates](/docs/reference/templates) | [Acknowledgments](/docs/reference/acknowledgments#traxanalyzers)
