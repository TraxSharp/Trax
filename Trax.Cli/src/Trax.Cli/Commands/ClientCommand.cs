using System.CommandLine;
using Trax.Cli.Clients;
using Trax.Cli.Machines;
using Trax.StateMachine;

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
        var manifest = new Option<FileInfo?>("--manifest")
        {
            Description =
                "The client manifest the host exported with --trax-export-client. Required unless --operations is given.",
        };
        var operations = new Option<FileInfo?>("--operations")
        {
            Description =
                "The operations schema, as SDL, in place of --manifest: generates a client for documents that reach "
                + "only operations and the operator's streams, as the operations console's do, and the "
                + "console-documents.json a host registers them from. Needs --documents.",
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
        var documents = new Option<string[]>("--documents")
        {
            Description =
                "A glob of the .graphql documents the frontend wrote (src/**/*.graphql), relative to the current "
                + "directory. Repeatable. Writes a typed function per operation and trax.documents.json, which the "
                + "host names in ClientDocuments(path).",
            AllowMultipleArgumentsPerToken = false,
        };
        var lang = new Option<string?>("--lang")
        {
            Description =
                "With --operations, the language of the client: typescript (default) or csharp, a client over "
                + "Trax.Client for a .NET operator frontend.",
        };
        var @namespace = new Option<string?>("--namespace")
        {
            Description =
                $"With --operations --lang csharp, the namespace of the client (default {ClientGenerator.DefaultOperationsNamespace}).",
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
            operations,
            output,
            check,
            specifier,
            toolsDir,
            documents,
            lang,
            @namespace,
            node,
        };
        command.SetAction(
            (parseResult, _) =>
            {
                var stdout = parseResult.InvocationConfiguration.Output;
                var stderr = parseResult.InvocationConfiguration.Error;
                return parseResult.GetValue(operations) is { } schema
                        ? RunOperationsAsync(
                            parseResult.GetValue(manifest)?.FullName,
                            schema.FullName,
                            parseResult.GetRequiredValue(output).FullName,
                            parseResult.GetValue(check),
                            parseResult.GetValue(documents) ?? [],
                            parseResult.GetValue(lang),
                            parseResult.GetValue(@namespace),
                            stdout,
                            stderr
                        )
                    : parseResult.GetValue(lang) is not null
                    || parseResult.GetValue(@namespace) is not null
                        ? RefuseAsync(
                            "--lang and --namespace choose the operations client's language; give them with --operations.",
                            stderr
                        )
                    : RunAsync(
                        parseResult.GetValue(target) ?? "react",
                        parseResult.GetValue(manifest)?.FullName,
                        parseResult.GetRequiredValue(output).FullName,
                        parseResult.GetValue(check),
                        parseResult.GetValue(specifier),
                        parseResult.GetValue(toolsDir)?.FullName,
                        new NodeRunner(parseResult.GetValue(node) ?? "node"),
                        stdout,
                        stderr,
                        parseResult.GetValue(documents) ?? []
                    );
            }
        );
        return command;
    }

    internal static async Task<int> RunAsync(
        string target,
        string? manifestPath,
        string outDir,
        bool check,
        string? specifier,
        string? toolsDir,
        INodeRunner node,
        TextWriter stdout,
        TextWriter stderr,
        IReadOnlyList<string>? documentGlobs = null
    )
    {
        try
        {
            if (!string.Equals(target, "react", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"--target '{target}' is not one trax client generates. Use react."
                );
            if (specifier is not null && !MachineGenerator.IsModuleSpecifier(specifier))
                throw new InvalidOperationException(
                    $"--specifier '{specifier}' is not an npm package name with an optional subpath."
                );
            if (manifestPath is null)
                throw new InvalidOperationException(
                    "Name the host's client manifest with --manifest, or the operations schema with --operations."
                );
            if (!File.Exists(manifestPath))
                throw new InvalidOperationException($"No client manifest at {manifestPath}.");
            IReadOnlyList<ClientDocumentFile>? documentFiles = null;
            IReadOnlyList<string>? editorGlobs = null;
            if (documentGlobs is { Count: > 0 })
            {
                var cwd = Directory.GetCurrentDirectory();
                // The schema written for the editor is a .graphql file too: never read it as a document.
                documentFiles = await ReadDocumentsAsync(
                        documentGlobs,
                        Path.GetFullPath(Path.Combine(outDir, ClientGenerator.EditorSchemaFileName))
                    )
                    .ConfigureAwait(false);
                editorGlobs = documentGlobs.Select(g => Glob.RelativeTo(cwd, outDir, g)).ToList();
            }
            var options = new ClientGenerateOptions(
                ClientManifest.Read(
                    await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false)
                ),
                specifier ?? ClientGenerator.DefaultSpecifier,
                toolsDir,
                documentFiles,
                editorGlobs
            );
            var generator = new ClientGenerator(node);

            if (check)
            {
                var drift = await generator.CheckAsync(options, outDir).ConfigureAwait(false);
                foreach (var file in drift)
                    await stdout
                        .WriteLineAsync(
                            $"  {(file.Status == DriftStatus.Missing ? "MISSING" : "DRIFT"), -8}{file.Path}"
                        )
                        .ConfigureAwait(false);
                if (drift.Count == 0)
                {
                    await stdout
                        .WriteLineAsync($"The client in {outDir} is what the manifest generates.")
                        .ConfigureAwait(false);
                    return 0;
                }
                await stderr
                    .WriteLineAsync(
                        "The client is stale: export the manifest from the host again and run 'trax client'."
                    )
                    .ConfigureAwait(false);
                return 1;
            }

            var written = await generator.WriteAsync(options, outDir).ConfigureAwait(false);
            foreach (var file in written)
                await stdout.WriteLineAsync($"  {file.Path}").ConfigureAwait(false);
            await stdout
                .WriteLineAsync($"Generated {written.Count} file(s) in {outDir}.")
                .ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    internal static async Task<int> RunOperationsAsync(
        string? manifestPath,
        string schemaPath,
        string outDir,
        bool check,
        IReadOnlyList<string> documentGlobs,
        string? lang,
        string? @namespace,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        try
        {
            var language = lang switch
            {
                null or "typescript" => OperationsLanguage.TypeScript,
                "csharp" => OperationsLanguage.CSharp,
                _ => throw new InvalidOperationException(
                    $"--lang '{lang}' is not one trax client writes. Use typescript or csharp."
                ),
            };
            if (@namespace is not null && language != OperationsLanguage.CSharp)
                throw new InvalidOperationException(
                    "--namespace names the C# client's namespace; give it with --lang csharp."
                );
            var ns = @namespace ?? ClientGenerator.DefaultOperationsNamespace;
            if (manifestPath is not null)
                throw new InvalidOperationException(
                    "--operations generates the operations client, which reads no manifest; leave out --manifest."
                );
            if (documentGlobs.Count == 0)
                throw new InvalidOperationException(
                    "--operations needs the documents it generates functions for: name them with --documents."
                );
            if (!File.Exists(schemaPath))
                throw new InvalidOperationException($"No operations schema at {schemaPath}.");
            var options = new OperationsGenerateOptions(
                await File.ReadAllTextAsync(schemaPath).ConfigureAwait(false),
                await ReadDocumentsAsync(documentGlobs, Path.GetFullPath(schemaPath))
                    .ConfigureAwait(false)
            );

            if (check)
            {
                var drift = await (
                    language == OperationsLanguage.CSharp
                        ? ClientGenerator.CheckCSharpAsync(options, ns, outDir)
                        : ClientGenerator.CheckAsync(options, outDir)
                ).ConfigureAwait(false);
                foreach (var file in drift)
                    await stdout
                        .WriteLineAsync(
                            $"  {(file.Status == DriftStatus.Missing ? "MISSING" : "DRIFT"), -8}{file.Path}"
                        )
                        .ConfigureAwait(false);
                if (drift.Count == 0)
                {
                    await stdout
                        .WriteLineAsync($"The client in {outDir} is what the documents generate.")
                        .ConfigureAwait(false);
                    return 0;
                }
                await stderr
                    .WriteLineAsync(
                        "The client is stale: run 'trax client --operations' again after changing a document or the schema."
                    )
                    .ConfigureAwait(false);
                return 1;
            }

            var written = await (
                language == OperationsLanguage.CSharp
                    ? ClientGenerator.WriteCSharpAsync(options, ns, outDir)
                    : ClientGenerator.WriteAsync(options, outDir)
            ).ConfigureAwait(false);
            foreach (var file in written)
                await stdout.WriteLineAsync($"  {file.Path}").ConfigureAwait(false);
            await stdout
                .WriteLineAsync($"Generated {written.Count} file(s) in {outDir}.")
                .ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    // The .graphql files the globs match under the current directory, by their path relative to it, never `exclude`
    // (a schema written as a .graphql file a glob would match too). The two are compared with their links resolved
    // and spelled as on disk, since the matches are under the working directory, which is reported that way, and
    // `exclude` may not be.
    private static async Task<List<ClientDocumentFile>> ReadDocumentsAsync(
        IReadOnlyList<string> globs,
        string exclude
    )
    {
        var cwd = Directory.GetCurrentDirectory();
        exclude = Glob.Resolved(exclude);
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var glob in globs)
        {
            var matches = Glob.Files(cwd, glob)
                .Where(f => !string.Equals(Glob.Resolved(f), exclude, StringComparison.Ordinal))
                .ToList();
            if (matches.Count == 0)
                throw new InvalidOperationException(
                    $"--documents '{glob}' matches no file under {cwd}."
                );
            found.UnionWith(matches);
        }
        var files = new List<ClientDocumentFile>();
        foreach (var path in found)
            files.Add(
                new ClientDocumentFile(
                    Path.GetRelativePath(cwd, path).Replace('\\', '/'),
                    await File.ReadAllTextAsync(path).ConfigureAwait(false)
                )
            );
        return files;
    }

    private static async Task<int> RefuseAsync(string message, TextWriter stderr)
    {
        await stderr.WriteLineAsync(message).ConfigureAwait(false);
        return 1;
    }
}
