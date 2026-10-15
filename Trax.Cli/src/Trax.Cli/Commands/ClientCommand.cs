using System.CommandLine;
using Trax.Cli.Clients;
using Trax.Cli.Machines;
using Trax.Effect.StateMachine;

namespace Trax.Cli.Commands;

/// <summary>
/// <c>trax client</c>: generates a frontend's client from the client manifest a host exports
/// (<c>--trax-export-client</c>), or with <c>--check</c> fails when the committed client is not what the manifest
/// generates. It reads only the file: no host, no database, no credential.
/// </summary>
internal static class ClientCommand
{
    public static Command Create()
    {
        var target = new Option<string>("--target")
        {
            Description = "The framework the client is for. Only react is generated today.",
            DefaultValueFactory = _ => "react",
        };
        var manifest = new Option<FileInfo>("--manifest")
        {
            Description = "The client manifest the host exported with --trax-export-client.",
            Required = true,
        };
        var output = new Option<DirectoryInfo>("--out")
        {
            Description =
                "The directory the client is written to, inside the frontend's source (src/trax).",
            Required = true,
        };
        var check = new Option<bool>("--check")
        {
            Description =
                "Write nothing; exit 1 when the client in --out is not what the manifest generates.",
        };
        var specifier = new Option<string?>("--specifier")
        {
            Description =
                "The module the client imports the runtime from (default @traxsharp/state-machine).",
        };
        var toolsDir = new Option<DirectoryInfo?>("--tools-dir")
        {
            Description =
                "The engine's tools/ directory, when this trax has no generators bundled (built from source).",
        };
        var node = new Option<string?>("--node")
        {
            Description =
                "The node executable that generates the twins: an absolute path, or a name on PATH (default: node).",
        };

        var command = new Command(
            "client",
            "Generate a frontend's typed client from the host's client manifest, or check the committed one."
        )
        {
            target,
            manifest,
            output,
            check,
            specifier,
            toolsDir,
            node,
        };
        command.SetAction(parseResult =>
            Run(
                parseResult.GetValue(target) ?? "react",
                parseResult.GetRequiredValue(manifest).FullName,
                parseResult.GetRequiredValue(output).FullName,
                parseResult.GetValue(check),
                parseResult.GetValue(specifier),
                parseResult.GetValue(toolsDir)?.FullName,
                new NodeRunner(parseResult.GetValue(node) ?? "node")
            )
        );
        return command;
    }

    internal static int Run(
        string target,
        string manifestPath,
        string outDir,
        bool check,
        string? specifier,
        string? toolsDir,
        INodeRunner node
    )
    {
        try
        {
            if (target != "react")
                throw new InvalidOperationException(
                    $"--target '{target}' is not one trax client generates. Use react."
                );
            if (specifier is not null && !MachineGenerator.IsModuleSpecifier(specifier))
                throw new InvalidOperationException(
                    $"--specifier '{specifier}' is not an npm package name with an optional subpath."
                );
            if (!File.Exists(manifestPath))
                throw new InvalidOperationException($"No client manifest at {manifestPath}.");
            var options = new ClientGenerateOptions(
                ClientManifest.Read(File.ReadAllText(manifestPath)),
                specifier ?? ClientGenerator.DefaultSpecifier,
                toolsDir
            );
            var generator = new ClientGenerator(node);

            if (check)
            {
                var drift = generator.Check(options, outDir);
                foreach (var file in drift)
                    Console.WriteLine(
                        $"  {(file.Status == DriftStatus.Missing ? "MISSING" : "DRIFT"), -8}{file.Path}"
                    );
                if (drift.Count == 0)
                {
                    Console.WriteLine($"The client in {outDir} is what the manifest generates.");
                    return 0;
                }
                Console.Error.WriteLine(
                    "The client is stale: export the manifest from the host again and run 'trax client'."
                );
                return 1;
            }

            var written = generator.Write(options, outDir);
            foreach (var file in written)
                Console.WriteLine($"  {file.Path}");
            Console.WriteLine($"Generated {written.Count} file(s) in {outDir}.");
            return 0;
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
