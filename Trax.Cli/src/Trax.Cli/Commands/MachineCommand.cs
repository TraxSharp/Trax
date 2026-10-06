using System.CommandLine;
using Trax.Cli.Machines;

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
        };

    private static Command CreateNew()
    {
        var nameArgument = new Argument<string>("name")
        {
            Description = "Machine name (kebab-case id, e.g. 'checkout' or 'write-to-congress').",
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
        var version = typeof(Trax.Effect.StateMachine.Persistence.IMachine)
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
            "  1. The machine derives from Machine<TState, TTrigger> in Trax.Effect.StateMachine.Persistence. In the",
            "     project that holds this file, reference the version this trax is built against:",
            $"       dotnet add package Trax.Effect.StateMachine.Persistence{versionFlag}",
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
            "Export the IR from a compiled machine and generate its TS twin and/or differential corpus."
        )
        {
            o.Assembly,
            o.Machine,
            o.IrOut,
            o.TwinOut,
            o.CorpusOut,
            o.EngineSrc,
            o.ImportStyle,
            o.Specifier,
            o.ToolsDir,
            o.Node,
        };
        command.SetAction(parseResult =>
            RunGenerate(
                parseResult.GetRequiredValue(o.Assembly).FullName,
                parseResult.GetValue(o.Machine),
                parseResult.GetValue(o.IrOut)?.FullName,
                parseResult.GetValue(o.TwinOut)?.FullName,
                parseResult.GetValue(o.CorpusOut)?.FullName,
                parseResult.GetValue(o.EngineSrc)?.FullName,
                parseResult.GetValue(o.ImportStyle),
                parseResult.GetValue(o.Specifier),
                parseResult.GetValue(o.ToolsDir)?.FullName,
                new NodeRunner(parseResult.GetValue(o.Node) ?? "node")
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
            o.Machine,
            o.IrOut,
            o.TwinOut,
            o.CorpusOut,
            o.EngineSrc,
            o.ImportStyle,
            o.Specifier,
            o.ToolsDir,
            o.Node,
        };
        command.SetAction(parseResult =>
            RunCheck(
                parseResult.GetRequiredValue(o.Assembly).FullName,
                parseResult.GetValue(o.Machine),
                parseResult.GetValue(o.IrOut)?.FullName,
                parseResult.GetValue(o.TwinOut)?.FullName,
                parseResult.GetValue(o.CorpusOut)?.FullName,
                parseResult.GetValue(o.EngineSrc)?.FullName,
                parseResult.GetValue(o.ImportStyle),
                parseResult.GetValue(o.Specifier),
                parseResult.GetValue(o.ToolsDir)?.FullName,
                new NodeRunner(parseResult.GetValue(o.Node) ?? "node")
            )
        );
        return command;
    }

    private static Command CreateShow()
    {
        var o = MachineOptions.Create();
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
        string assemblyPath,
        string? machineName,
        string? irOut,
        string? twinOut,
        string? corpusOut,
        string? engineSrc,
        string? importStyle,
        string? specifier,
        string? toolsDir,
        INodeRunner node
    )
    {
        try
        {
            ValidateImportStyle(importStyle);
            var machine = MachineLoader.Load(assemblyPath, machineName);
            var result = new MachineGenerator(node).Generate(
                new MachineGenerateOptions(
                    machine,
                    irOut,
                    twinOut,
                    corpusOut,
                    engineSrc,
                    importStyle ?? "relative",
                    specifier,
                    toolsDir
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
        string assemblyPath,
        string? machineName,
        string? irOut,
        string? twinOut,
        string? corpusOut,
        string? engineSrc,
        string? importStyle,
        string? specifier,
        string? toolsDir,
        INodeRunner node
    )
    {
        try
        {
            ValidateImportStyle(importStyle);
            var machine = MachineLoader.Load(assemblyPath, machineName);
            var result = new MachineGenerator(node).Check(
                new MachineGenerateOptions(
                    machine,
                    irOut,
                    twinOut,
                    corpusOut,
                    engineSrc,
                    importStyle ?? "relative",
                    specifier,
                    toolsDir
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

    private static void ValidateImportStyle(string? importStyle)
    {
        if (importStyle is not null and not "relative" and not "specifier")
            throw new InvalidOperationException(
                $"--import-style must be 'relative' or 'specifier', got '{importStyle}'."
            );
    }

    /// <summary>The options shared by <c>generate</c> and <c>check</c>. A fresh set per command, since a
    /// System.CommandLine option belongs to one command.</summary>
    private sealed record MachineOptions(
        Option<FileInfo> Assembly,
        Option<string?> Machine,
        Option<DirectoryInfo?> IrOut,
        Option<DirectoryInfo?> TwinOut,
        Option<DirectoryInfo?> CorpusOut,
        Option<DirectoryInfo?> EngineSrc,
        Option<string?> ImportStyle,
        Option<string?> Specifier,
        Option<DirectoryInfo?> ToolsDir,
        Option<string?> Node
    )
    {
        public static MachineOptions Create() =>
            new(
                new Option<FileInfo>("--assembly")
                {
                    Description = "Compiled assembly (.dll) containing the machine.",
                    Required = true,
                },
                new Option<string?>("--machine")
                {
                    Description =
                        "Full type name of the machine (required only if the assembly has more than one).",
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
                        "The TypeScript engine's src/ directory (required for the twin and corpus).",
                },
                new Option<string?>("--import-style")
                {
                    Description = "Twin engine imports: 'relative' (default) or 'specifier'.",
                },
                new Option<string?>("--specifier")
                {
                    Description =
                        "Module specifier when --import-style specifier (default @trax/state-machine).",
                },
                new Option<DirectoryInfo?>("--tools-dir")
                {
                    Description =
                        "The engine's tools/ directory (default: sibling of --engine-src).",
                },
                new Option<string?>("--node")
                {
                    Description = "Path to the node executable (default: node).",
                }
            );
    }
}
