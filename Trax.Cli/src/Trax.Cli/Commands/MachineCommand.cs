using System.CommandLine;
using System.Globalization;
using Trax.Cli.Machines;
using Trax.StateMachine.Gherkin;

namespace Trax.Cli.Commands;

/// <summary>
/// The <c>trax machine</c> command group: export a compiled Trax machine's IR (in-process) and generate its
/// TypeScript twin and differential corpus (by shelling out to the engine's node entrypoints). Replaces the
/// fragile regenerate-via-golden-tests script a consumer runs on every machine edit.
/// </summary>
internal static class MachineCommand
{
    public static async Task<Command> CreateAsync()
    {
        // The --lang help lists the languages the bundled twin generator writes, read from the assembly.
        var langHelp = await TwinLanguages.HelpAsync().ConfigureAwait(false);
        return new(
            "machine",
            "Generate and check state-machine artifacts from a compiled Trax machine."
        )
        {
            CreateNew(),
            CreateGenerate(langHelp),
            CreateCheck(langHelp),
            CreateShow(langHelp),
            CreateMigrate(),
            CreateFromGherkin(),
            CreateRun(),
            CreateMissing(),
            CreateUsage(),
            CreateExplain(),
            CreateDiagram(),
        };
    }

    private static Command CreateNew()
    {
        var nameArgument = new Argument<string>("name")
        {
            Description = "Machine name (kebab-case id, e.g. 'checkout' or 'ship-to-store').",
        };
        var outputOption = new Option<DirectoryInfo?>("--output")
        {
            Description = "Directory to write <Name>Machine.cs (default: current directory).",
        };
        var namespaceOption = new Option<string?>("--namespace")
        {
            Description = "Namespace for the generated file (default: Machines).",
        };
        var withEffectOption = new Option<bool>("--with-effect")
        {
            Description =
                "Include an exactly-once effect stub and mark the terminal state committed.",
        };
        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite the file if it already exists.",
        };

        var command = new Command(
            "new",
            "Scaffold a new Tier-1 machine as a single declarative C# file."
        )
        {
            nameArgument,
            outputOption,
            namespaceOption,
            withEffectOption,
            forceOption,
        };
        command.SetAction(
            (parseResult, _) =>
                RunNewAsync(
                    parseResult.GetRequiredValue(nameArgument),
                    parseResult.GetValue(outputOption)?.FullName ?? Directory.GetCurrentDirectory(),
                    parseResult.GetValue(namespaceOption),
                    parseResult.GetValue(withEffectOption),
                    parseResult.GetValue(forceOption),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error
                )
        );
        return command;
    }

    internal static async Task<int> RunNewAsync(
        string name,
        string outputDir,
        string? @namespace,
        bool withEffect,
        bool force,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        try
        {
            var path = await MachineScaffolder
                .WriteAsync(name, outputDir, @namespace ?? "Machines", withEffect, force)
                .ConfigureAwait(false);
            await stdout.WriteLineAsync($"Scaffolded {path}").ConfigureAwait(false);
            await stdout.WriteLineAsync(NewNextSteps()).ConfigureAwait(false);
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>
    /// What a scaffolded machine needs before <c>generate</c> can use it, spelled out because none of it is
    /// visible from the file: the package its base class lives in, a build, and node plus the engine for the
    /// TypeScript artifacts.
    /// </summary>
    internal static string NewNextSteps()
    {
        var version = typeof(Trax.StateMachine.Persistence.IMachine)
            .Assembly.GetCustomAttributes(
                typeof(System.Reflection.AssemblyInformationalVersionAttribute),
                inherit: false
            )
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion.Split('+')[0];
        var versionFlag = version is null ? "" : $" --version {version}";
        return string.Join(
            Environment.NewLine,
            "Next steps:",
            "  1. The machine derives from Machine<TState, TTrigger> in Trax.StateMachine.Persistence. In the",
            "     project that holds this file, reference the version this trax is built against:",
            $"       dotnet add package Trax.StateMachine.Persistence{versionFlag}",
            "  2. Edit the states, triggers and context, then build the project.",
            "  3. Export the IR from the built assembly:",
            "       trax machine generate --assembly <path to the built .dll> --ir-out <dir>",
            "     The TypeScript twin (--twin-out) and differential corpus (--corpus-out) also need Node.js 22 or",
            "     later on PATH and --engine-src pointing at the TypeScript state-machine engine's src/ directory."
        );
    }

    private static Command CreateGenerate(string langHelp)
    {
        var o = MachineOptions.Create(langHelp);
        var command = new Command(
            "generate",
            "Export the IR from a compiled machine, or read it from a host's client manifest, and generate its typed view (TypeScript or C#) and/or differential corpus."
        )
        {
            o.Assembly,
            o.Manifest,
            o.Machine,
            o.IrOut,
            o.TwinOut,
            o.CorpusOut,
            o.EngineSrc,
            o.ImportStyle,
            o.Specifier,
            o.ToolsDir,
            o.Node,
            o.Lang,
            o.Namespace,
        };
        command.SetAction(
            (parseResult, _) =>
                RunGenerateAsync(
                    parseResult.GetValue(o.Assembly)?.FullName,
                    parseResult.GetValue(o.Machine),
                    parseResult.GetValue(o.IrOut)?.FullName,
                    parseResult.GetValue(o.TwinOut)?.FullName,
                    parseResult.GetValue(o.CorpusOut)?.FullName,
                    parseResult.GetValue(o.EngineSrc)?.FullName,
                    parseResult.GetValue(o.ImportStyle),
                    parseResult.GetValue(o.Specifier),
                    parseResult.GetValue(o.ToolsDir)?.FullName,
                    new NodeRunner(parseResult.GetValue(o.Node) ?? "node"),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error,
                    parseResult.GetValue(o.Lang),
                    parseResult.GetValue(o.Namespace),
                    parseResult.GetValue(o.Manifest)?.FullName
                )
        );
        return command;
    }

    private static Command CreateCheck(string langHelp)
    {
        var o = MachineOptions.Create(langHelp);
        var command = new Command(
            "check",
            "Regenerate to a temp location and fail (exit 1) if any committed artifact has drifted."
        )
        {
            o.Assembly,
            o.Manifest,
            o.Machine,
            o.IrOut,
            o.TwinOut,
            o.CorpusOut,
            o.EngineSrc,
            o.ImportStyle,
            o.Specifier,
            o.ToolsDir,
            o.Node,
            o.Lang,
            o.Namespace,
        };
        command.SetAction(
            (parseResult, _) =>
                RunCheckAsync(
                    parseResult.GetValue(o.Assembly)?.FullName,
                    parseResult.GetValue(o.Machine),
                    parseResult.GetValue(o.IrOut)?.FullName,
                    parseResult.GetValue(o.TwinOut)?.FullName,
                    parseResult.GetValue(o.CorpusOut)?.FullName,
                    parseResult.GetValue(o.EngineSrc)?.FullName,
                    parseResult.GetValue(o.ImportStyle),
                    parseResult.GetValue(o.Specifier),
                    parseResult.GetValue(o.ToolsDir)?.FullName,
                    new NodeRunner(parseResult.GetValue(o.Node) ?? "node"),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error,
                    parseResult.GetValue(o.Lang),
                    parseResult.GetValue(o.Namespace),
                    parseResult.GetValue(o.Manifest)?.FullName
                )
        );
        return command;
    }

    private static Command CreateShow(string langHelp)
    {
        var o = MachineOptions.Create(langHelp, assemblyRequired: true);
        var formatOption = new Option<string?>("--format")
        {
            Description =
                "text (default, for a terminal), mermaid (a stateDiagram-v2) or dot (Graphviz).",
        };
        var asciiOption = new Option<bool>("--ascii")
        {
            Description = "Draw the text format with ASCII only, for terminals without box glyphs.",
        };
        var command = new Command(
            "show",
            "Print a compiled machine's states and transitions: initial and committed states, guards, reducers and effects."
        )
        {
            o.Assembly,
            o.Machine,
            formatOption,
            asciiOption,
        };
        command.SetAction(
            (parseResult, _) =>
                RunShowAsync(
                    parseResult.GetRequiredValue(o.Assembly).FullName,
                    parseResult.GetValue(o.Machine),
                    parseResult.GetValue(formatOption),
                    parseResult.GetValue(asciiOption),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error,
                    UseColor()
                )
        );
        return command;
    }

    // Colour only for a terminal, and never when NO_COLOR is set (https://no-color.org).
    private static bool UseColor() =>
        !Console.IsOutputRedirected
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

    internal static async Task<int> RunShowAsync(
        string assemblyPath,
        string? machineName,
        string? format,
        bool ascii,
        TextWriter output,
        TextWriter stderr,
        bool color = false
    )
    {
        try
        {
            var parsedFormat = format?.ToLowerInvariant() switch
            {
                null or "text" => MachineFormat.Text,
                "mermaid" => MachineFormat.Mermaid,
                "dot" => MachineFormat.Dot,
                _ => throw new InvalidOperationException(
                    $"--format must be 'text', 'mermaid' or 'dot', got '{format}'."
                ),
            };
            var machine = MachineLoader.Load(assemblyPath, machineName);
            string ir;
            try
            {
                ir = machine.ExportIr();
            }
            catch (ArgumentException ex)
            {
                // The engine refuses a malformed machine when it is built, which exporting triggers.
                throw new InvalidOperationException(ex.Message, ex);
            }
            await output
                .WriteAsync(MachineRenderer.Render(ir, parsedFormat, ascii, color))
                .ConfigureAwait(false);
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private static Command CreateFromGherkin()
    {
        var featuresOption = new Option<DirectoryInfo>("--features")
        {
            Description = "Directory of .feature files, read recursively.",
            Required = true,
        };
        var glossaryOption = new Option<FileInfo?>("--glossary")
        {
            Description =
                "A glossary's JSON file, for features not yet written with a lexicon. Without it, the features directory's lexicon is read.",
        };
        var trainsOption = TrainsOption();
        var outOption = new Option<DirectoryInfo>("--out")
        {
            Description =
                "Directory the command owns: one <machine>.ir.json per machine, host.json, access.txt and the glossary.json the lexicon elaborates to. A machine that no longer derives has its file removed.",
            Required = true,
        };
        var checkOption = new Option<bool>("--check")
        {
            Description =
                "Write nothing; exit 1 if any file in --out differs from what the features derive, access.txt included.",
        };
        var command = new Command(
            "from-gherkin",
            "Derive machines from features and their lexicon: their IRs, the host file, the access report and the glossary."
        )
        {
            featuresOption,
            glossaryOption,
            trainsOption,
            outOption,
            checkOption,
        };
        command.SetAction(
            (parseResult, _) =>
            {
                var features = parseResult.GetRequiredValue(featuresOption).FullName;
                return RunFromGherkinAsync(
                    features,
                    parseResult.GetValue(glossaryOption)?.FullName,
                    parseResult.GetRequiredValue(outOption).FullName,
                    parseResult.GetValue(checkOption),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error,
                    Trains(parseResult, trainsOption)
                );
            }
        );
        return command;
    }

    internal static async Task<int> RunFromGherkinAsync(
        string featuresDir,
        string? glossaryPath,
        string outDir,
        bool check,
        TextWriter stdout,
        TextWriter stderr,
        IReadOnlyList<string>? trains = null
    )
    {
        try
        {
            FeatureDirectory.RefuseForeign(outDir);
            if (
                await RefuseGlossaryInsideOutAsync(glossaryPath, outDir, stderr)
                    .ConfigureAwait(false)
            )
                return 1;
            var result = await FeatureDirectory
                .DeriveAsync(featuresDir, glossaryPath, trains)
                .ConfigureAwait(false);
            foreach (var step in result.UiSteps)
                await stdout
                    .WriteLineAsync($"not derived (about the UI): {step}")
                    .ConfigureAwait(false);
            // A warning is printed and stops nothing, --check included.
            foreach (var warning in result.Warnings)
                await stderr.WriteLineAsync($"warning: {warning}").ConfigureAwait(false);
            if (result.Problems.Count > 0)
            {
                foreach (var problem in result.Problems)
                    await stderr.WriteLineAsync(problem).ConfigureAwait(false);
                await stderr
                    .WriteLineAsync(
                        $"{result.Problems.Count} problem(s); nothing was {(check ? "checked" : "written")}."
                    )
                    .ConfigureAwait(false);
                return 1;
            }
            return await CheckOrWriteDerivedAsync(outDir, result.Files, check, stdout, stderr)
                .ConfigureAwait(false);
        }
        catch (FeatureMachinesException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (IOException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    // --out is the command's: what it does not derive there it removes, a glossary it was told to read included.
    // True when the glossary is inside --out, once that is written to stderr.
    private static async Task<bool> RefuseGlossaryInsideOutAsync(
        string? glossaryPath,
        string outDir,
        TextWriter stderr
    )
    {
        if (
            glossaryPath is not null
            && Path.GetFullPath(glossaryPath)
                .StartsWith(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(outDir))
                        + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal
                )
        )
        {
            await stderr
                .WriteLineAsync(
                    $"--glossary {glossaryPath} is inside --out {outDir}, which from-gherkin owns and would remove "
                        + "it from. Keep a hand-written glossary outside --out, or write the lexicon it elaborates from."
                )
                .ConfigureAwait(false);
            return true;
        }
        return false;
    }

    private static async Task<int> CheckOrWriteDerivedAsync(
        string outDir,
        IReadOnlyDictionary<string, string> files,
        bool check,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        if (check)
        {
            var drift = await FeatureDirectory.CheckAsync(outDir, files).ConfigureAwait(false);
            foreach (var line in drift)
                await stderr.WriteLineAsync(line).ConfigureAwait(false);
            if (drift.Count > 0)
            {
                await stderr
                    .WriteLineAsync(
                        "Run `trax machine from-gherkin` without --check and commit what it writes."
                    )
                    .ConfigureAwait(false);
                return 1;
            }
            await stdout.WriteLineAsync($"{outDir} is up to date.").ConfigureAwait(false);
            return 0;
        }
        var changes = await FeatureDirectory.WriteAsync(outDir, files).ConfigureAwait(false);
        foreach (var line in changes)
            await stdout.WriteLineAsync(line).ConfigureAwait(false);
        if (changes.Count == 0)
            await stdout.WriteLineAsync($"{outDir} is up to date.").ConfigureAwait(false);
        return 0;
    }

    private static Command CreateRun()
    {
        var options = new RunOptions();
        var command = new Command(
            "run",
            "Run features in memory on the machines a directory serves, access included, answering as the host does."
        )
        {
            options.Features,
            options.Glossary,
            options.TrainAssemblies,
            options.Machines,
            options.Locations,
            options.Tags,
            options.Name,
            options.Format,
            options.Report,
            options.Strict,
            options.DryRun,
            options.FailFast,
        };
        command.SetAction(
            (parseResult, _) =>
                RunRunAsync(
                    options.Arguments(parseResult),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error
                )
        );
        return command;
    }

    // The options of 'trax machine run', and the arguments they give.
    private sealed class RunOptions
    {
        public RunOptions() => Format.AcceptOnlyFromAmong(RunReportWriter.Formats);

        public Option<DirectoryInfo> Features { get; } =
            new("--features")
            {
                Description = "Directory of .feature files, read recursively.",
                Required = true,
            };
        public Option<FileInfo?> Glossary { get; } =
            new("--glossary")
            {
                Description =
                    "A glossary's JSON file, for features not yet written with a lexicon. Without it, the features directory's lexicon is read.",
            };
        public Option<FileInfo[]> TrainAssemblies { get; } = TrainsOption();
        public Option<DirectoryInfo> Machines { get; } =
            new("--machines")
            {
                Description =
                    "The directory from-gherkin wrote, as committed: the features run on these machines.",
                Required = true,
            };
        public Argument<string[]> Locations { get; } =
            new("locations")
            {
                Description =
                    "Scenarios to run, as file:line relative to --features (a scenario's or an example row's line).",
                Arity = ArgumentArity.ZeroOrMore,
            };
        public Option<string?> Tags { get; } =
            new("--tags")
            {
                Description =
                    "A tag expression: @smoke, '@smoke and not @slow', '(@a or @b) and not @c'.",
            };
        public Option<string[]> Name { get; } =
            new("--name")
            {
                Description =
                    "Run only scenarios whose name matches this regular expression. Repeat for any of several.",
                AllowMultipleArgumentsPerToken = false,
            };
        public Option<string> Format { get; } =
            new("--format")
            {
                Description = "pretty (default), json or junit.",
                DefaultValueFactory = _ => "pretty",
            };
        public Option<FileInfo?> Report { get; } =
            new("--report")
            {
                Description = "Write the report to this file instead of standard output.",
            };
        public Option<bool> Strict { get; } =
            new("--strict")
            {
                Description =
                    "Also fail on steps about the UI, which are not checked here. An undefined step always fails.",
            };
        public Option<bool> DryRun { get; } =
            new("--dry-run")
            {
                Description = "Read every step and run none: shows undefined steps.",
            };
        public Option<bool> FailFast { get; } =
            new("--fail-fast") { Description = "Stop at the first scenario that does not pass." };

        public RunArguments Arguments(ParseResult parseResult)
        {
            var features = parseResult.GetRequiredValue(Features).FullName;
            return new RunArguments(
                features,
                parseResult.GetValue(Glossary)?.FullName,
                parseResult.GetRequiredValue(Machines).FullName,
                parseResult.GetValue(Locations) ?? [],
                parseResult.GetValue(Tags),
                parseResult.GetValue(Name) ?? [],
                parseResult.GetValue(Format) ?? "pretty",
                parseResult.GetValue(Report)?.FullName,
                parseResult.GetValue(Strict),
                parseResult.GetValue(DryRun),
                parseResult.GetValue(FailFast)
            )
            {
                Trains = Trains(parseResult, TrainAssemblies),
            };
        }
    }

    internal sealed record RunArguments(
        string FeaturesDir,
        string? GlossaryPath,
        string MachinesDir,
        IReadOnlyList<string> Locations,
        string? Tags = null,
        IReadOnlyList<string>? Names = null,
        string Format = "pretty",
        string? ReportPath = null,
        bool Strict = false,
        bool DryRun = false,
        bool FailFast = false
    )
    {
        /// <summary>Train assemblies the lexicon reads each train's records from.</summary>
        public IReadOnlyList<string> Trains { get; init; } = [];
    }

    private static Task WriteSlowNamePatternAsync(
        System.Text.RegularExpressions.RegexMatchTimeoutException ex,
        TextWriter stderr
    ) =>
        stderr.WriteLineAsync(
            string.Create(
                CultureInfo.InvariantCulture,
                $"--name '{ex.Pattern}' took longer than {ex.MatchTimeout.TotalSeconds:0.#} s to match a scenario's name; give a simpler expression."
            )
        );

    internal static async Task<int> RunRunAsync(
        RunArguments args,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        try
        {
            var read = await FeatureDirectory
                .ReadFeaturesAsync(args.FeaturesDir, args.GlossaryPath, args.Trains)
                .ConfigureAwait(false);
            var (glossary, features) = (read.Glossary.Inner, read.Features);
            var (host, irs) = await Trax
                .StateMachine.Persistence.FromIr.IrDirectory.ReadAsync(args.MachinesDir)
                .ConfigureAwait(false);
            var locations = await ParseLocationsAsync(args.Locations, stderr).ConfigureAwait(false);
            if (
                locations is null
                || !await ValidateSelectionAsync(args, stderr).ConfigureAwait(false)
            )
                return 1;

            var report = await FeatureRun
                .RunAsync(
                    glossary,
                    features,
                    irs,
                    host,
                    new RunSelection(args.Tags, args.Names, locations),
                    args.DryRun,
                    args.FailFast
                )
                .ConfigureAwait(false);
            return await ReportRunAsync(report, args, stdout, stderr).ConfigureAwait(false);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException ex)
        {
            await WriteSlowNamePatternAsync(ex, stderr).ConfigureAwait(false);
            return 1;
        }
        catch (FeatureMachinesException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (IOException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    // The --locations as (file, line), or null once one that is not file:line is written to stderr.
    private static async Task<List<(string File, long Line)>?> ParseLocationsAsync(
        IReadOnlyList<string> argLocations,
        TextWriter stderr
    )
    {
        var locations = new List<(string File, long Line)>();
        foreach (var location in argLocations)
        {
            var colon = location.LastIndexOf(':');
            if (
                colon <= 0
                || !long.TryParse(
                    location[(colon + 1)..],
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var line
                )
                || line < 1
            )
            {
                await stderr
                    .WriteLineAsync($"'{location}' is not file:line, such as ordering.feature:12.")
                    .ConfigureAwait(false);
                return null;
            }
            locations.Add((location[..colon].Replace('\\', '/'), line));
        }
        return locations;
    }

    // False once a --name that is not a regular expression, or --tags that do not parse, is written to stderr.
    private static async Task<bool> ValidateSelectionAsync(RunArguments args, TextWriter stderr)
    {
        if (args.Names is { } names)
            foreach (var name in names)
                try
                {
                    _ = new System.Text.RegularExpressions.Regex(
                        name,
                        System.Text.RegularExpressions.RegexOptions.None,
                        TimeSpan.FromSeconds(1)
                    );
                }
                catch (ArgumentException ex)
                {
                    await stderr
                        .WriteLineAsync(
                            $"--name '{name}' is not a regular expression: {ex.Message}"
                        )
                        .ConfigureAwait(false);
                    return false;
                }
        if (args.Tags is { } tags)
            try
            {
                TagExpression.Parse(tags);
            }
            catch (FormatException ex)
            {
                await stderr.WriteLineAsync($"--tags {ex.Message}").ConfigureAwait(false);
                return false;
            }
        return true;
    }

    private static async Task<int> ReportRunAsync(
        RunReport report,
        RunArguments args,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        foreach (var problem in report.Problems)
            await stderr.WriteLineAsync(problem.ToString()).ConfigureAwait(false);
        var text = RunReportWriter.Write(report, args.Format);
        if (args.ReportPath is { } path)
            await File.WriteAllTextAsync(
                    path,
                    text,
                    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                )
                .ConfigureAwait(false);
        else
            await stdout.WriteAsync(text).ConfigureAwait(false);

        // A selection that runs nothing is never a pass: no scenario matched (a typo in a tag or a location), or
        // every one it matched is @host, which only the host bindings run. A @host scenario beside one that ran is
        // skipped and does not fail the run.
        var ran = report.Scenarios.Any(s => s.Status is not ScenarioStatus.Skipped);
        var passed =
            report.Problems.Count == 0
            && ran
            && report.Scenarios.All(s =>
                (s.Status is ScenarioStatus.Passed or ScenarioStatus.Skipped)
                && (
                    !args.Strict
                    || !s.Skipped.Any(x =>
                        !string.Equals(x.Message, "not run", StringComparison.Ordinal)
                    )
                )
            );
        if (report.Scenarios.Count == 0)
            await stderr.WriteLineAsync("No scenario matched.").ConfigureAwait(false);
        else if (!ran)
            await stderr
                .WriteLineAsync(
                    $"Every scenario selected ({report.Scenarios.Count}) is tagged @host, which only the host bindings run, so nothing ran."
                )
                .ConfigureAwait(false);
        return passed ? 0 : 1;
    }

    private static (
        Option<DirectoryInfo> Features,
        Option<FileInfo?> Glossary,
        Option<FileInfo[]> Trains
    ) FeatureOptions() =>
        (
            new Option<DirectoryInfo>("--features")
            {
                Description = "Directory of .feature files, read recursively.",
                Required = true,
            },
            new Option<FileInfo?>("--glossary")
            {
                Description =
                    "A glossary's JSON file, for features not yet written with a lexicon. Without it, the features directory's lexicon is read.",
            },
            TrainsOption()
        );

    private static Option<FileInfo[]> TrainsOption() =>
        new("--trains")
        {
            Description =
                "A train assembly (.dll) the lexicon reads each train's input and output records from, as the host does, so its Returns: and Takes: lines are optional. Repeat for several.",
            AllowMultipleArgumentsPerToken = false,
        };

    private static IReadOnlyList<string> Trains(
        ParseResult parseResult,
        Option<FileInfo[]> option
    ) => (parseResult.GetValue(option) ?? []).Select(f => f.FullName).ToList();

    private static (string Features, string? Glossary, IReadOnlyList<string> Trains) FeaturePaths(
        ParseResult parseResult,
        (
            Option<DirectoryInfo> Features,
            Option<FileInfo?> Glossary,
            Option<FileInfo[]> Trains
        ) options
    ) =>
        (
            parseResult.GetRequiredValue(options.Features).FullName,
            parseResult.GetValue(options.Glossary)?.FullName,
            Trains(parseResult, options.Trains)
        );

    private static Command CreateMissing()
    {
        var paths = FeatureOptions();
        var kindOption = new Option<string[]>("--kind")
        {
            Description =
                "Only these gaps: machine, trigger, state, guard, refusal, outcome, view, grant. Repeat for several.",
        };
        kindOption.AcceptOnlyFromAmong(
            Enum.GetNames<GapKind>().Select(n => n.ToLowerInvariant()).ToArray()
        );
        var outOption = new Option<FileInfo?>("--out")
        {
            Description = "Also write the stubs to this file, as one feature to paste from.",
        };
        var command = new Command(
            "missing",
            "List what the glossary names and no scenario shows, each with a scenario to paste that is checked green first."
        )
        {
            paths.Features,
            paths.Glossary,
            paths.Trains,
            kindOption,
            outOption,
        };
        command.SetAction(
            (parseResult, _) =>
            {
                var (features, glossary, trains) = FeaturePaths(parseResult, paths);
                return RunMissingAsync(
                    features,
                    glossary,
                    parseResult.GetValue(kindOption) ?? [],
                    parseResult.GetValue(outOption)?.FullName,
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error,
                    trains
                );
            }
        );
        return command;
    }

    internal static async Task<int> RunMissingAsync(
        string featuresDir,
        string? glossaryPath,
        IReadOnlyList<string> kinds,
        string? outPath,
        TextWriter stdout,
        TextWriter stderr,
        IReadOnlyList<string>? trains = null
    )
    {
        try
        {
            var read = await FeatureDirectory
                .ReadFeaturesAsync(featuresDir, glossaryPath, trains)
                .ConfigureAwait(false);
            var (gaps, problems) = await Missing
                .FindAsync(read.Glossary.Inner, read.Features)
                .ConfigureAwait(false);
            if (problems.Count > 0)
            {
                foreach (var problem in problems)
                    await stderr.WriteLineAsync(problem.ToString()).ConfigureAwait(false);
                await stderr
                    .WriteLineAsync("The features do not derive: fix these first.")
                    .ConfigureAwait(false);
                return 1;
            }
            var wanted = gaps.Where(g =>
                    kinds.Count == 0
                    || kinds.Contains(g.Kind.ToString(), StringComparer.OrdinalIgnoreCase)
                )
                .ToList();
            foreach (var gap in wanted)
            {
                await stdout.WriteLineAsync($"# {gap.Message}").ConfigureAwait(false);
                await stdout
                    .WriteAsync(gap.Stub ?? "#   (no scenario can be written for this one)\n")
                    .ConfigureAwait(false);
                await stdout.WriteLineAsync().ConfigureAwait(false);
            }
            await stdout.WriteLineAsync($"{wanted.Count} gap(s).").ConfigureAwait(false);
            if (outPath is not null)
                await WriteStubsAsync(outPath, wanted).ConfigureAwait(false);
            return 0;
        }
        catch (FeatureMachinesException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (IOException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    // The stubs, as one feature to paste from.
    private static async Task WriteStubsAsync(string outPath, List<Gap> wanted)
    {
        var file = new System.Text.StringBuilder("Feature: Missing scenarios\n");
        foreach (var gap in wanted.Where(g => g.Stub is not null))
            file.Append($"\n  # {gap.Message}\n").Append(gap.Stub);
        await File.WriteAllTextAsync(
                outPath,
                file.ToString(),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            )
            .ConfigureAwait(false);
    }

    private static Command CreateUsage()
    {
        var paths = FeatureOptions();
        var unusedOption = new Option<bool>("--unused")
        {
            Description = "Only the phrases no step says.",
        };
        var command = new Command("usage", "List each glossary phrase and the steps that say it.")
        {
            paths.Features,
            paths.Glossary,
            paths.Trains,
            unusedOption,
        };
        command.SetAction(
            (parseResult, _) =>
            {
                var (features, glossary, trains) = FeaturePaths(parseResult, paths);
                return RunUsageAsync(
                    features,
                    glossary,
                    parseResult.GetValue(unusedOption),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error,
                    trains
                );
            }
        );
        return command;
    }

    internal static async Task<int> RunUsageAsync(
        string featuresDir,
        string? glossaryPath,
        bool unusedOnly,
        TextWriter stdout,
        TextWriter stderr,
        IReadOnlyList<string>? trains = null
    )
    {
        try
        {
            var read = await FeatureDirectory
                .ReadFeaturesAsync(featuresDir, glossaryPath, trains)
                .ConfigureAwait(false);
            var (phrases, problems) = Usage.Find(read.Glossary.Inner, read.Features);
            foreach (var problem in problems)
                await stderr.WriteLineAsync(problem.ToString()).ConfigureAwait(false);
            foreach (var use in phrases.Where(p => !unusedOnly || p.Steps.Count == 0))
            {
                await stdout
                    .WriteLineAsync($"{use.Part} \"{use.Phrase}\": {use.Steps.Count}")
                    .ConfigureAwait(false);
                foreach (var (file, line) in use.Steps)
                    await stdout
                        .WriteLineAsync(
                            string.Create(CultureInfo.InvariantCulture, $"  {file}:{line}")
                        )
                        .ConfigureAwait(false);
            }
            return problems.Count == 0 ? 0 : 1;
        }
        catch (FeatureMachinesException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (IOException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private static Command CreateExplain()
    {
        var paths = FeatureOptions();
        var scenarioOption = new Option<string?>("--scenario")
        {
            Description =
                "Only the scenarios whose name contains this text (any case), or the one at this file:line.",
        };
        var command = new Command(
            "explain",
            "Derive in memory and print, for each scenario, what derivation read from it: edges, guards, outcomes, timers, policies, starts, grants and views."
        )
        {
            paths.Features,
            paths.Glossary,
            paths.Trains,
            scenarioOption,
        };
        command.SetAction(
            (parseResult, _) =>
            {
                var (features, glossary, trains) = FeaturePaths(parseResult, paths);
                return RunExplainAsync(
                    features,
                    glossary,
                    parseResult.GetValue(scenarioOption),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error,
                    trains
                );
            }
        );
        return command;
    }

    internal static async Task<int> RunExplainAsync(
        string featuresDir,
        string? glossaryPath,
        string? scenario,
        TextWriter stdout,
        TextWriter stderr,
        IReadOnlyList<string>? trains = null
    )
    {
        try
        {
            var result = await FeatureDirectory
                .DeriveAsync(featuresDir, glossaryPath, trains)
                .ConfigureAwait(false);
            if (result.Problems.Count > 0)
            {
                foreach (var problem in result.Problems)
                    await stderr.WriteLineAsync(problem).ConfigureAwait(false);
                await stderr
                    .WriteLineAsync(
                        $"{result.Problems.Count} problem(s): the features do not derive, so there is nothing to explain."
                    )
                    .ConfigureAwait(false);
                return 1;
            }
            var shown = SelectScenarios(result.Files, scenario);
            if (scenario is not null && shown.Count == 0)
            {
                await stderr
                    .WriteLineAsync($"No scenario that derived anything matches '{scenario}'.")
                    .ConfigureAwait(false);
                return 1;
            }
            await WriteExplanationAsync(shown, result, stdout, stderr).ConfigureAwait(false);
            return 0;
        }
        catch (FeatureMachinesException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (IOException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    // The scenarios that derived anything, or with a scenario only those whose name or file:line it matches.
    private static List<ScenarioExplanation.Scenario> SelectScenarios(
        IReadOnlyDictionary<string, string> files,
        string? scenario
    ) =>
        ScenarioExplanation
            .Explain(files)
            .Where(s =>
                scenario is null
                || s.Name.Contains(scenario, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    string.Create(CultureInfo.InvariantCulture, $"{s.File}:{s.Line}"),
                    scenario,
                    StringComparison.Ordinal
                )
            )
            .ToList();

    private static async Task WriteExplanationAsync(
        List<ScenarioExplanation.Scenario> shown,
        FeatureDirectory.Result result,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        foreach (var s in shown)
        {
            await stdout
                .WriteLineAsync(
                    string.Create(CultureInfo.InvariantCulture, $"{s.File}:{s.Line} {s.Name}")
                )
                .ConfigureAwait(false);
            foreach (var fact in s.Facts)
                await stdout.WriteLineAsync($"  {fact}").ConfigureAwait(false);
        }
        foreach (var step in result.UiSteps)
            await stdout
                .WriteLineAsync($"not derived (about the UI): {step}")
                .ConfigureAwait(false);
        foreach (var warning in result.Warnings)
            await stderr.WriteLineAsync($"warning: {warning}").ConfigureAwait(false);
    }

    private static Command CreateDiagram()
    {
        var machinesOption = new Option<DirectoryInfo>("--machines")
        {
            Description = "The directory from-gherkin wrote.",
            Required = true,
        };
        var formatOption = new Option<string>("--format")
        {
            Description = "html (default, one page that needs no network), mermaid or json.",
            DefaultValueFactory = _ => "html",
        };
        formatOption.AcceptOnlyFromAmong(BoardWriter.Formats);
        var outOption = new Option<FileInfo?>("--out")
        {
            Description = "Write the board to this file instead of standard output.",
        };
        var command = new Command(
            "diagram",
            "Draw the event-storming board of a machines directory: actors, commands, events, policies, views, and the scenarios behind each."
        )
        {
            machinesOption,
            formatOption,
            outOption,
        };
        command.SetAction(
            (parseResult, _) =>
                RunDiagramAsync(
                    parseResult.GetRequiredValue(machinesOption).FullName,
                    parseResult.GetValue(formatOption) ?? "html",
                    parseResult.GetValue(outOption)?.FullName,
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error
                )
        );
        return command;
    }

    internal static async Task<int> RunDiagramAsync(
        string machinesDir,
        string format,
        string? outPath,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        try
        {
            var (host, irs) = await Trax
                .StateMachine.Persistence.FromIr.IrDirectory.ReadAsync(machinesDir)
                .ConfigureAwait(false);
            var text = BoardWriter.Write(Trax.StateMachine.Features.Board.Draw(irs, host), format);
            if (outPath is null)
                await stdout.WriteAsync(text).ConfigureAwait(false);
            else
                await File.WriteAllTextAsync(
                        outPath,
                        text,
                        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                    )
                    .ConfigureAwait(false);
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (IOException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    private static Command CreateMigrate()
    {
        var command = new Command(
            "migrate",
            "Scaffold a forward migration by diffing the context schema (not implemented yet)."
        );
        command.SetAction(
            (parseResult, _) => RunMigrateAsync(parseResult.InvocationConfiguration.Error)
        );
        return command;
    }

    // Migrations are not implemented, so the command fails: a CI step that runs it must not pass.
    internal static async Task<int> RunMigrateAsync(TextWriter stderr)
    {
        await stderr
            .WriteLineAsync(
                "trax machine migrate is not implemented yet: a machine's IR does not carry migrations. A stored "
                    + "snapshot whose version does not match the current machine is rejected as a typed "
                    + "version-mismatch and the client starts fresh."
            )
            .ConfigureAwait(false);
        return 1;
    }

    // Returns the process exit code (0 success, 1 failure). The action returns it so InvokeAsync propagates it;
    // setting Environment.ExitCode would not work here because Program returns InvokeAsync's result, which wins.
    internal static async Task<int> RunGenerateAsync(
        string? assemblyPath,
        string? machineName,
        string? irOut,
        string? twinOut,
        string? corpusOut,
        string? engineSrc,
        string? importStyle,
        string? specifier,
        string? toolsDir,
        INodeRunner node,
        TextWriter stdout,
        TextWriter stderr,
        string? lang = null,
        string? @namespace = null,
        string? manifestPath = null
    )
    {
        try
        {
            var machine = await SourceAsync(assemblyPath, manifestPath, machineName)
                .ConfigureAwait(false);
            var result = await new MachineGenerator(node)
                .GenerateAsync(
                    new MachineGenerateOptions(
                        machine,
                        irOut,
                        twinOut,
                        corpusOut,
                        engineSrc,
                        importStyle,
                        specifier,
                        toolsDir,
                        lang,
                        @namespace
                    )
                )
                .ConfigureAwait(false);

            foreach (var artifact in result.Written)
                await stdout
                    .WriteLineAsync($"  {artifact.Kind, -9} {artifact.Path}")
                    .ConfigureAwait(false);
            await stdout
                .WriteLineAsync(
                    $"Generated {result.Written.Count} artifact(s) for '{machine.Name}'."
                )
                .ConfigureAwait(false);
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    internal static async Task<int> RunCheckAsync(
        string? assemblyPath,
        string? machineName,
        string? irOut,
        string? twinOut,
        string? corpusOut,
        string? engineSrc,
        string? importStyle,
        string? specifier,
        string? toolsDir,
        INodeRunner node,
        TextWriter stdout,
        TextWriter stderr,
        string? lang = null,
        string? @namespace = null,
        string? manifestPath = null
    )
    {
        try
        {
            var machine = await SourceAsync(assemblyPath, manifestPath, machineName)
                .ConfigureAwait(false);
            var result = await new MachineGenerator(node)
                .CheckAsync(
                    new MachineGenerateOptions(
                        machine,
                        irOut,
                        twinOut,
                        corpusOut,
                        engineSrc,
                        importStyle,
                        specifier,
                        toolsDir,
                        lang,
                        @namespace
                    )
                )
                .ConfigureAwait(false);

            foreach (var check in result.Checks)
            {
                var label = check.Status switch
                {
                    DriftStatus.UpToDate => "ok",
                    DriftStatus.Drifted => "DRIFT",
                    DriftStatus.Missing => "MISSING",
                    _ => "?",
                };
                await stdout
                    .WriteLineAsync($"  {label, -8}{check.Kind, -9} {check.Path}")
                    .ConfigureAwait(false);
            }

            if (result.IsClean)
            {
                await stdout
                    .WriteLineAsync($"All artifacts for '{machine.Name}' are up to date.")
                    .ConfigureAwait(false);
                return 0;
            }

            await stderr
                .WriteLineAsync(
                    "State-machine artifacts are stale. Run 'trax machine generate' to update them."
                )
                .ConfigureAwait(false);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>
    /// The machine whose IR <c>generate</c> and <c>check</c> read: compiled into <paramref name="assemblyPath"/>, or
    /// in the client manifest at <paramref name="manifestPath"/>. Exactly one of the two.
    /// </summary>
    internal static async Task<MachineIr> SourceAsync(
        string? assemblyPath,
        string? manifestPath,
        string? machineName
    ) =>
        (assemblyPath, manifestPath) switch
        {
            (not null, null) => MachineIr.Of(MachineLoader.Load(assemblyPath, machineName)),
            (null, not null) => await MachineIr
                .FromManifestAsync(manifestPath, machineName)
                .ConfigureAwait(false),
            (null, null) => throw new InvalidOperationException(
                "Pass --assembly with the compiled machine, or --manifest with a host's client manifest."
            ),
            _ => throw new InvalidOperationException(
                "--assembly and --manifest are two places to read the IR from; pass one of them."
            ),
        };

    /// <summary>The options shared by <c>generate</c> and <c>check</c>. A fresh set per command, since a
    /// System.CommandLine option belongs to one command.</summary>
    private sealed record MachineOptions(
        Option<FileInfo> Assembly,
        Option<FileInfo?> Manifest,
        Option<string?> Machine,
        Option<DirectoryInfo?> IrOut,
        Option<DirectoryInfo?> TwinOut,
        Option<DirectoryInfo?> CorpusOut,
        Option<DirectoryInfo?> EngineSrc,
        Option<string?> ImportStyle,
        Option<string?> Specifier,
        Option<DirectoryInfo?> ToolsDir,
        Option<string?> Node,
        Option<string?> Lang,
        Option<string?> Namespace
    )
    {
        public static MachineOptions Create(string langHelp, bool assemblyRequired = false) =>
            new(
                AssemblyOption(assemblyRequired),
                new Option<FileInfo?>("--manifest")
                {
                    Description =
                        "The client manifest the host exported with --trax-export-client, to read the IR from in place of --assembly: for a frontend that has the manifest and not the server.",
                },
                new Option<string?>("--machine")
                {
                    Description =
                        "Full type name of the machine, or its id with --manifest (required only if there is more than one).",
                },
                new Option<DirectoryInfo?>("--ir-out")
                {
                    Description = "Directory to write <id>.ir.json.",
                },
                new Option<DirectoryInfo?>("--twin-out")
                {
                    Description = "Directory to write <id>.contexts.g.ts and <id>.machine.g.ts.",
                },
                new Option<DirectoryInfo?>("--corpus-out")
                {
                    Description = "Directory to write differential.json.",
                },
                new Option<DirectoryInfo?>("--engine-src")
                {
                    Description =
                        "Generate the twin and corpus from this engine checkout's src/ (and the tools/ beside it) instead of the engine bundled into trax.",
                },
                new Option<string?>("--import-style")
                {
                    Description =
                        "Twin engine imports: 'specifier' (default) or 'relative' (default with --engine-src, which it needs).",
                },
                new Option<string?>("--specifier")
                {
                    Description =
                        "Module specifier when --import-style specifier: an npm package name, optionally with a subpath (default @traxsharp/state-machine).",
                },
                new Option<DirectoryInfo?>("--tools-dir")
                {
                    Description =
                        "The generators' directory (default: the tools/ beside --engine-src, else the ones bundled into trax). Needs --engine-src.",
                },
                NodeOption(),
                new Option<string?>("--lang") { Description = langHelp },
                new Option<string?>("--namespace")
                {
                    Description =
                        "The C# view's namespace with --lang csharp (default Trax.Machines).",
                }
            );

        private static Option<FileInfo> AssemblyOption(bool assemblyRequired) =>
            new("--assembly")
            {
                Description = assemblyRequired
                    ? "Compiled assembly (.dll) containing the machine."
                    : "Compiled assembly (.dll) containing the machine. Or pass --manifest.",
                Required = assemblyRequired,
            };

        private static Option<string?> NodeOption() =>
            new("--node")
            {
                Description =
                    "The node executable: an absolute path, or a name looked up on PATH (default: node). Never resolved from the current directory; a relative path is refused, and on Windows so is a .cmd or .bat shim.",
            };
    }
}
