# Trax.Analyzers

Opt-in Roslyn rules for code that builds on Trax .NET, or any other .NET code. About 250 checks
(culture and comparer bugs, async and cancellation mistakes, regex timeouts, the null-forgiving
operator and more), plus bans on reading the clock, chance or the console from library code.

Every rule in the profile is an error. Nothing is on until you reference the package.

```xml
<PackageReference Include="Trax.Analyzers" PrivateAssets="all" />
```

## Turning things off

One rule, in `.editorconfig` (anything you configure wins over the profile):

```ini
[*.cs]
dotnet_diagnostic.TRX0048.severity = none
```

A whole group, with an MSBuild property in the project or `Directory.Build.props`:

| Property | `false` turns off |
|---|---|
| `TraxAnalyzers` | the whole profile |
| `TraxAnalyzersTests` | the profile and the bans in test projects |
| `TraxBanClock` | `DateTime.Now` and friends, `Stopwatch`, the tick counts |
| `TraxBanRandom` | `Random.Shared`, `new Random()` |
| `TraxBanConsole` | `System.Console` |
| `TraxBanProcessExit` | `Environment.Exit`, `Environment.FailFast` |
| `TraxBanNullForgiving` | the `!` operator (`null!` and `default!` are rule TRX0191) |

`TraxAnalysisLevel` pins the profile to a level, so a later release cannot add errors to your build
until you raise it.

Each rule has a page under [`docs/rules/`](docs/rules/), and `rules.txt` lists every rule with the
severity the profile gives it.

## Where the rules come from

The rules are a copy of [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer)
(MIT), renamed into Trax's namespace and ids (`MA0004` is `TRX0004`). Trax maintains the copy and
references none of its packages. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
