using System.CommandLine;
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
    public static Command Create() =>
        new("machine", "Generate and check state-machine artifacts from a compiled Trax machine.")
        {
            CreateNew(),
            CreateGenerate(),
            CreateCheck(),
            CreateShow(),
            CreateMigrate(),
            CreateFromGherkin(),
            CreateRun(),
            CreateMissing(),
            CreateUsage(),
            CreateDiagram(),
        };

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
        command.SetAction(parseResult =>
            RunNew(
                parseResult.GetRequiredValue(nameArgument),
                parseResult.GetValue(outputOption)?.FullName ?? Directory.GetCurrentDirectory(),
                parseResult.GetValue(namespaceOption),
                parseResult.GetValue(withEffectOption),
                parseResult.GetValue(forceOption)
            )
        );
        return command;
    }

    internal static int RunNew(
        string name,
        string outputDir,
        string? @namespace,
        bool withEffect,
        bool force
    )
    {
        try
        {
            var path = MachineScaffolder.Write(
                name,
                outputDir,
                @namespace ?? "Machines",
                withEffect,
                force
            );
            Console.WriteLine($"Scaffolded {path}");
            Console.WriteLine(NewNextSteps());
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
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
                false
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

    private static Command CreateGenerate()
    {
        var o = MachineOptions.Create();
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
        command.SetAction(parseResult =>
            RunGenerate(
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
                parseResult.GetValue(o.Lang),
                parseResult.GetValue(o.Namespace),
                parseResult.GetValue(o.Manifest)?.FullName
            )
        );
        return command;
    }

    private static Command CreateCheck()
    {
        var o = MachineOptions.Create();
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
        command.SetAction(parseResult =>
            RunCheck(
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
                parseResult.GetValue(o.Lang),
                parseResult.GetValue(o.Namespace),
                parseResult.GetValue(o.Manifest)?.FullName
            )
        );
        return command;
    }

    private static Command CreateShow()
    {
        var o = MachineOptions.Create(assemblyRequired: true);
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
        command.SetAction(parseResult =>
            RunShow(
                parseResult.GetRequiredValue(o.Assembly).FullName,
                parseResult.GetValue(o.Machine),
                parseResult.GetValue(formatOption),
                parseResult.GetValue(asciiOption),
                Console.Out,
                UseColor()
            )
        );
        return command;
    }

    // Colour only for a terminal, and never when NO_COLOR is set (https://no-color.org).
    private static bool UseColor() =>
        !Console.IsOutputRedirected
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

    internal static int RunShow(
        string assemblyPath,
        string? machineName,
        string? format,
        bool ascii,
        TextWriter output,
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
            output.Write(MachineRenderer.Render(ir, parsedFormat, ascii, color));
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
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
                "The glossary's JSON file (default: glossary.json in the features directory).",
        };
        var outOption = new Option<DirectoryInfo>("--out")
        {
            Description =
                "Directory the command owns: one <machine>.ir.json per machine, host.json and access.txt. A machine that no longer derives has its file removed.",
            Required = true,
        };
        var checkOption = new Option<bool>("--check")
        {
            Description =
                "Write nothing; exit 1 if any file in --out differs from what the features derive, access.txt included.",
        };
        var command = new Command(
            "from-gherkin",
            "Derive machines from features and a glossary: their IRs, the host file and the access report."
        )
        {
            featuresOption,
            glossaryOption,
            outOption,
            checkOption,
        };
        command.SetAction(parseResult =>
        {
            var features = parseResult.GetRequiredValue(featuresOption).FullName;
            return RunFromGherkin(
                features,
                parseResult.GetValue(glossaryOption)?.FullName
                    ?? Path.Combine(features, "glossary.json"),
                parseResult.GetRequiredValue(outOption).FullName,
                parseResult.GetValue(checkOption),
                Console.Out,
                Console.Error
            );
        });
        return command;
    }

    internal static int RunFromGherkin(
        string featuresDir,
        string glossaryPath,
        string outDir,
        bool check,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        try
        {
            FeatureDirectory.RefuseForeign(outDir);
            var result = FeatureDirectory.Derive(featuresDir, glossaryPath);
            foreach (var step in result.UiSteps)
                stdout.WriteLine($"not derived (about the UI): {step}");
            if (result.Problems.Count > 0)
            {
                foreach (var problem in result.Problems)
                    stderr.WriteLine(problem);
                stderr.WriteLine(
                    $"{result.Problems.Count} problem(s); nothing was {(check ? "checked" : "written")}."
                );
                return 1;
            }
            if (check)
            {
                var drift = FeatureDirectory.Check(outDir, result.Files);
                foreach (var line in drift)
                    stderr.WriteLine(line);
                if (drift.Count > 0)
                {
                    stderr.WriteLine(
                        "Run `trax machine from-gherkin` without --check and commit what it writes."
                    );
                    return 1;
                }
                stdout.WriteLine($"{outDir} is up to date.");
                return 0;
            }
            var changes = FeatureDirectory.Write(outDir, result.Files);
            foreach (var line in changes)
                stdout.WriteLine(line);
            if (changes.Count == 0)
                stdout.WriteLine($"{outDir} is up to date.");
            return 0;
        }
        catch (FeatureMachinesException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
        catch (IOException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
    }

    private static Command CreateRun()
    {
        var featuresOption = new Option<DirectoryInfo>("--features")
        {
            Description = "Directory of .feature files, read recursively.",
            Required = true,
        };
        var glossaryOption = new Option<FileInfo?>("--glossary")
        {
            Description =
                "The glossary's JSON file (default: glossary.json in the features directory).",
        };
        var machinesOption = new Option<DirectoryInfo>("--machines")
        {
            Description =
                "The directory from-gherkin wrote, as committed: the features run on these machines.",
            Required = true,
        };
        var locationsArgument = new Argument<string[]>("locations")
        {
            Description =
                "Scenarios to run, as file:line relative to --features (a scenario's or an example row's line).",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var tagsOption = new Option<string?>("--tags")
        {
            Description =
                "A tag expression: @smoke, '@smoke and not @slow', '(@a or @b) and not @c'.",
        };
        var nameOption = new Option<string[]>("--name")
        {
            Description =
                "Run only scenarios whose name matches this regular expression. Repeat for any of several.",
            AllowMultipleArgumentsPerToken = false,
        };
        var formatOption = new Option<string>("--format")
        {
            Description = "pretty (default), json or junit.",
            DefaultValueFactory = _ => "pretty",
        };
        formatOption.AcceptOnlyFromAmong(RunReportWriter.Formats);
        var reportOption = new Option<FileInfo?>("--report")
        {
            Description = "Write the report to this file instead of standard output.",
        };
        var strictOption = new Option<bool>("--strict")
        {
            Description =
                "Also fail on steps about the UI, which are not checked here. An undefined step always fails.",
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Read every step and run none: shows undefined steps.",
        };
        var failFastOption = new Option<bool>("--fail-fast")
        {
            Description = "Stop at the first scenario that does not pass.",
        };
        var command = new Command(
            "run",
            "Run features in memory on the machines a directory serves, access included, answering as the host does."
        )
        {
            featuresOption,
            glossaryOption,
            machinesOption,
            locationsArgument,
            tagsOption,
            nameOption,
            formatOption,
            reportOption,
            strictOption,
            dryRunOption,
            failFastOption,
        };
        command.SetAction(parseResult =>
        {
            var features = parseResult.GetRequiredValue(featuresOption).FullName;
            return RunRun(
                new RunArguments(
                    features,
                    parseResult.GetValue(glossaryOption)?.FullName
                        ?? Path.Combine(features, "glossary.json"),
                    parseResult.GetRequiredValue(machinesOption).FullName,
                    parseResult.GetValue(locationsArgument) ?? [],
                    parseResult.GetValue(tagsOption),
                    parseResult.GetValue(nameOption) ?? [],
                    parseResult.GetValue(formatOption) ?? "pretty",
                    parseResult.GetValue(reportOption)?.FullName,
                    parseResult.GetValue(strictOption),
                    parseResult.GetValue(dryRunOption),
                    parseResult.GetValue(failFastOption)
                ),
                Console.Out,
                Console.Error
            );
        });
        return command;
    }

    internal sealed record RunArguments(
        string FeaturesDir,
        string GlossaryPath,
        string MachinesDir,
        IReadOnlyList<string> Locations,
        string? Tags = null,
        IReadOnlyList<string>? Names = null,
        string Format = "pretty",
        string? ReportPath = null,
        bool Strict = false,
        bool DryRun = false,
        bool FailFast = false
    );

    internal static int RunRun(RunArguments args, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            var (glossary, features) = FeatureDirectory.Read(args.FeaturesDir, args.GlossaryPath);
            var (host, irs) = Trax.StateMachine.Persistence.FromIr.IrDirectory.Read(
                args.MachinesDir
            );
            var locations = new List<(string File, long Line)>();
            foreach (var location in args.Locations)
            {
                var colon = location.LastIndexOf(':');
                if (colon <= 0 || !long.TryParse(location[(colon + 1)..], out var line) || line < 1)
                {
                    stderr.WriteLine(
                        $"'{location}' is not file:line, such as ordering.feature:12."
                    );
                    return 1;
                }
                locations.Add((location[..colon].Replace('\\', '/'), line));
            }
            if (args.Names is { } names)
                foreach (var name in names)
                    try
                    {
                        _ = new System.Text.RegularExpressions.Regex(name);
                    }
                    catch (ArgumentException ex)
                    {
                        stderr.WriteLine(
                            $"--name '{name}' is not a regular expression: {ex.Message}"
                        );
                        return 1;
                    }
            if (args.Tags is { } tags)
                try
                {
                    TagExpression.Parse(tags);
                }
                catch (FormatException ex)
                {
                    stderr.WriteLine($"--tags {ex.Message}");
                    return 1;
                }

            var report = FeatureRun.Run(
                glossary,
                features,
                irs,
                host,
                new RunSelection(args.Tags, args.Names, locations),
                args.DryRun,
                args.FailFast
            );
            foreach (var problem in report.Problems)
                stderr.WriteLine(problem);
            var text = RunReportWriter.Write(report, args.Format);
            if (args.ReportPath is { } path)
                File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
            else
                stdout.Write(text);

            var passed =
                report.Problems.Count == 0
                && report.Scenarios.All(s =>
                    s.Status == ScenarioStatus.Passed
                    && (!args.Strict || !s.Skipped.Any(x => x.Message != "not run"))
                );
            if (report.Scenarios.Count == 0)
                stderr.WriteLine("No scenario matched.");
            return passed ? 0 : 1;
        }
        catch (FeatureMachinesException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
        catch (IOException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
    }

    private static (Option<DirectoryInfo> Features, Option<FileInfo?> Glossary) FeatureOptions() =>
        (
            new Option<DirectoryInfo>("--features")
            {
                Description = "Directory of .feature files, read recursively.",
                Required = true,
            },
            new Option<FileInfo?>("--glossary")
            {
                Description =
                    "The glossary's JSON file (default: glossary.json in the features directory).",
            }
        );

    private static (string Features, string Glossary) FeaturePaths(
        ParseResult parseResult,
        (Option<DirectoryInfo> Features, Option<FileInfo?> Glossary) options
    )
    {
        var features = parseResult.GetRequiredValue(options.Features).FullName;
        return (
            features,
            parseResult.GetValue(options.Glossary)?.FullName
                ?? Path.Combine(features, "glossary.json")
        );
    }

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
            kindOption,
            outOption,
        };
        command.SetAction(parseResult =>
        {
            var (features, glossary) = FeaturePaths(parseResult, paths);
            return RunMissing(
                features,
                glossary,
                parseResult.GetValue(kindOption) ?? [],
                parseResult.GetValue(outOption)?.FullName,
                Console.Out,
                Console.Error
            );
        });
        return command;
    }

    internal static int RunMissing(
        string featuresDir,
        string glossaryPath,
        IReadOnlyList<string> kinds,
        string? outPath,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        try
        {
            var (glossary, features) = FeatureDirectory.Read(featuresDir, glossaryPath);
            var (gaps, problems) = Missing.Find(glossary, features);
            if (problems.Count > 0)
            {
                foreach (var problem in problems)
                    stderr.WriteLine(problem);
                stderr.WriteLine("The features do not derive: fix these first.");
                return 1;
            }
            var wanted = gaps.Where(g =>
                    kinds.Count == 0
                    || kinds.Contains(g.Kind.ToString(), StringComparer.OrdinalIgnoreCase)
                )
                .ToList();
            foreach (var gap in wanted)
            {
                stdout.WriteLine($"# {gap.Message}");
                stdout.Write(gap.Stub ?? "#   (no scenario can be written for this one)\n");
                stdout.WriteLine();
            }
            stdout.WriteLine($"{wanted.Count} gap(s).");
            if (outPath is not null)
            {
                var file = new System.Text.StringBuilder("Feature: Missing scenarios\n");
                foreach (var gap in wanted.Where(g => g.Stub is not null))
                    file.Append($"\n  # {gap.Message}\n").Append(gap.Stub);
                File.WriteAllText(outPath, file.ToString(), new System.Text.UTF8Encoding(false));
            }
            return 0;
        }
        catch (FeatureMachinesException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
        catch (IOException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
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
            unusedOption,
        };
        command.SetAction(parseResult =>
        {
            var (features, glossary) = FeaturePaths(parseResult, paths);
            return RunUsage(
                features,
                glossary,
                parseResult.GetValue(unusedOption),
                Console.Out,
                Console.Error
            );
        });
        return command;
    }

    internal static int RunUsage(
        string featuresDir,
        string glossaryPath,
        bool unusedOnly,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        try
        {
            var (glossary, features) = FeatureDirectory.Read(featuresDir, glossaryPath);
            var (phrases, problems) = Usage.Find(glossary, features);
            foreach (var problem in problems)
                stderr.WriteLine(problem);
            foreach (var use in phrases.Where(p => !unusedOnly || p.Steps.Count == 0))
            {
                stdout.WriteLine($"{use.Part} \"{use.Phrase}\": {use.Steps.Count}");
                foreach (var (file, line) in use.Steps)
                    stdout.WriteLine($"  {file}:{line}");
            }
            return problems.Count == 0 ? 0 : 1;
        }
        catch (FeatureMachinesException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
        catch (IOException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
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
        command.SetAction(parseResult =>
            RunDiagram(
                parseResult.GetRequiredValue(machinesOption).FullName,
                parseResult.GetValue(formatOption) ?? "html",
                parseResult.GetValue(outOption)?.FullName,
                Console.Out,
                Console.Error
            )
        );
        return command;
    }

    internal static int RunDiagram(
        string machinesDir,
        string format,
        string? outPath,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        try
        {
            var (host, irs) = Trax.StateMachine.Persistence.FromIr.IrDirectory.Read(machinesDir);
            var text = BoardWriter.Write(Trax.StateMachine.Features.Board.Draw(irs, host), format);
            if (outPath is null)
                stdout.Write(text);
            else
                File.WriteAllText(outPath, text, new System.Text.UTF8Encoding(false));
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
        catch (IOException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            stderr.WriteLine(ex.Message);
            return 1;
        }
    }

    private static Command CreateMigrate()
    {
        var command = new Command(
            "migrate",
            "Scaffold a forward migration by diffing the context schema (not implemented yet)."
        );
        command.SetAction(_ => RunMigrate());
        return command;
    }

    // Migrations are not implemented, so the command fails: a CI step that runs it must not pass.
    internal static int RunMigrate()
    {
        Console.Error.WriteLine(
            "trax machine migrate is not implemented yet: a machine's IR does not carry migrations. A stored "
                + "snapshot whose version does not match the current machine is rejected as a typed "
                + "version-mismatch and the client starts fresh."
        );
        return 1;
    }

    // Returns the process exit code (0 success, 1 failure). The action returns it so InvokeAsync propagates it;
    // setting Environment.ExitCode would not work here because Program returns InvokeAsync's result, which wins.
    internal static int RunGenerate(
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
        string? lang = null,
        string? @namespace = null,
        string? manifestPath = null
    )
    {
        try
        {
            var machine = Source(assemblyPath, manifestPath, machineName);
            var result = new MachineGenerator(node).Generate(
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
            );

            foreach (var artifact in result.Written)
                Console.WriteLine($"  {artifact.Kind, -9} {artifact.Path}");
            Console.WriteLine(
                $"Generated {result.Written.Count} artifact(s) for '{machine.Name}'."
            );
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    internal static int RunCheck(
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
        string? lang = null,
        string? @namespace = null,
        string? manifestPath = null
    )
    {
        try
        {
            var machine = Source(assemblyPath, manifestPath, machineName);
            var result = new MachineGenerator(node).Check(
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
            );

            foreach (var check in result.Checks)
            {
                var label = check.Status switch
                {
                    DriftStatus.UpToDate => "ok",
                    DriftStatus.Drifted => "DRIFT",
                    DriftStatus.Missing => "MISSING",
                    _ => "?",
                };
                Console.WriteLine($"  {label, -8}{check.Kind, -9} {check.Path}");
            }

            if (result.IsClean)
            {
                Console.WriteLine($"All artifacts for '{machine.Name}' are up to date.");
                return 0;
            }

            Console.Error.WriteLine(
                "State-machine artifacts are stale. Run 'trax machine generate' to update them."
            );
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// The machine whose IR <c>generate</c> and <c>check</c> read: compiled into <paramref name="assemblyPath"/>, or
    /// in the client manifest at <paramref name="manifestPath"/>. Exactly one of the two.
    /// </summary>
    internal static MachineIr Source(
        string? assemblyPath,
        string? manifestPath,
        string? machineName
    ) =>
        (assemblyPath, manifestPath) switch
        {
            (not null, null) => MachineIr.Of(MachineLoader.Load(assemblyPath, machineName)),
            (null, not null) => MachineIr.FromManifest(manifestPath, machineName),
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
        public static MachineOptions Create(bool assemblyRequired = false) =>
            new(
                new Option<FileInfo>("--assembly")
                {
                    Description = assemblyRequired
                        ? "Compiled assembly (.dll) containing the machine."
                        : "Compiled assembly (.dll) containing the machine. Or pass --manifest.",
                    Required = assemblyRequired,
                },
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
                new Option<string?>("--node")
                {
                    Description =
                        "The node executable: an absolute path, or a name looked up on PATH (default: node). Never resolved from the current directory; a relative path is refused, and on Windows so is a .cmd or .bat shim.",
                },
                new Option<string?>("--lang") { Description = TwinLanguages.Help() },
                new Option<string?>("--namespace")
                {
                    Description =
                        "The C# view's namespace with --lang csharp (default Trax.Machines).",
                }
            );
    }
}
