using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using Trax.Cli.Deploy;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Commands;

/// <summary>
/// <c>trax deploy</c>: turns the projects named in <c>trax.deploy.json</c>, and what each host reports it needs
/// (<c>--trax-export-deploy</c>), into a deployment: a compose stack, an AWS environment
/// (<see cref="DeployAwsCommands"/>) or a Kubernetes one (<see cref="DeployKubernetesCommands"/>).
/// <c>trax deploy init</c> writes a first <c>trax.deploy.json</c>.
/// </summary>
internal static class DeployCommand
{
    public static Command Create()
    {
        var target = new Option<string?>("--target")
        {
            Description =
                "Where to deploy without --env: compose (the default) writes a compose stack for a rootless podman "
                + "host. With --env the environment's target decides, and --target, if given, must match it.",
        };
        var env = DeployAwsCommands.EnvOption(required: false);
        var file = DeployAwsCommands.FileOption();
        var output = new Option<DirectoryInfo?>("--out")
        {
            Description =
                "Where compose writes compose.yaml and .env.example (default: beside the file).",
        };
        var build = new Option<bool>("--build")
        {
            Description =
                "Also build each service's image with dotnet publish -t:PublishContainer, tagged latest.",
        };
        var force = new Option<bool>("--force")
        {
            Description =
                "Replace the hosts even when work is still busy at the end of the drain's timeout (drainTimeout, "
                + "ten minutes by default). Without it the deploy refuses and lists what is busy. On compose, "
                + "set TRAX_DRAIN_FORCE=true for the compose up instead.",
        };

        var command = new Command(
            "deploy",
            "Deploy the projects named in trax.deploy.json, from what each host reports it needs."
        )
        {
            target,
            env,
            file,
            output,
            build,
            force,
            CreateInit(),
        };
        foreach (var subcommand in DeployAwsCommands.Create())
            command.Subcommands.Add(subcommand);
        command.SetAction(
            (parseResult, ct) =>
                RunActionAsync(parseResult, target, env, file, output, build, force, ct)
        );
        return command;
    }

    private static Task<int> RunActionAsync(
        ParseResult parseResult,
        Option<string?> target,
        Option<string> env,
        Option<FileInfo?> file,
        Option<DirectoryInfo?> output,
        Option<bool> build,
        Option<bool> force,
        CancellationToken ct
    ) =>
        DispatchAsync(
            parseResult.GetValue(file)?.FullName
                ?? Path.Combine(Directory.GetCurrentDirectory(), DeployFile.FileName),
            parseResult.GetValue(target),
            parseResult.GetValue(env),
            parseResult.GetValue(output)?.FullName,
            parseResult.GetValue(build),
            new DotnetHostExporter(ct),
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error,
            ct,
            parseResult.GetValue(force)
        );

    /// <summary>
    /// Picks the target: with <paramref name="environment"/>, the environment's own, which a <paramref name="target"/>
    /// given beside it must match; without, <paramref name="target"/> or compose.
    /// </summary>
    internal static async Task<int> DispatchAsync(
        string path,
        string? target,
        string? environment,
        string? outDir,
        bool build,
        IHostExporter exporter,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct,
        bool force = false
    )
    {
        if (force && environment is null)
            await stdout.WriteLineAsync(ComposeForce).ConfigureAwait(false);
        if (environment is null)
            return await DispatchWithoutEnvironmentAsync(
                    path,
                    target,
                    outDir,
                    build,
                    exporter,
                    stdout,
                    stderr
                )
                .ConfigureAwait(false);
        string chosen;
        try
        {
            var file = await DeployFile.ReadAsync(path).ConfigureAwait(false);
            chosen = file.Environment(environment).Target ?? "";
            if (target is not null && !string.Equals(target, chosen, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"--target {target} does not match environment '{environment}', whose target is {chosen}. "
                        + "Leave --target out, or change the environment's target."
                );
        }
        catch (Exception ex) when (Reported(ex))
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
        return chosen switch
        {
            DeployEnvironment.Aws => await DeployAwsCommands
                .DeployAsync(path, environment, exporter, stdout, stderr, ct, force)
                .ConfigureAwait(false),
            DeployEnvironment.Kubernetes => await DeployKubernetesCommands
                .DeployAsync(path, environment, exporter, stdout, stderr, ct, force)
                .ConfigureAwait(false),
            _ => await ComposeAsync().ConfigureAwait(false),
        };

        async Task<int> ComposeAsync()
        {
            if (force)
                await stdout.WriteLineAsync(ComposeForce).ConfigureAwait(false);
            return await RunAsync(
                    DeployEnvironment.Compose,
                    path,
                    outDir,
                    build,
                    exporter,
                    BuildImageAsync,
                    stdout,
                    stderr,
                    environment
                )
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// How a release is run on compose: the migration and the drain first, waited for, then the rest. A plain
    /// <c>compose up</c> stops every container whose image changed before it starts any, so the old hosts would be
    /// gone before the drain began; the first two commands leave them running until it has finished.
    /// </summary>
    internal static string ComposeRelease(string engine) =>
        $"{engine} compose up -d migrate drain && {engine} compose wait drain && {engine} compose up -d";

    /// <summary>What <c>--force</c> means for compose, which trax writes rather than runs.</summary>
    internal const string ComposeForce =
        "--force changes nothing in the compose file: the drain runs when you start it with compose. To replace "
        + "the services while work is still busy, run that release once with TRAX_DRAIN_FORCE=true set.";

    private static async Task<int> DispatchWithoutEnvironmentAsync(
        string path,
        string? target,
        string? outDir,
        bool build,
        IHostExporter exporter,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        switch (target ?? DeployEnvironment.Compose)
        {
            case DeployEnvironment.Aws:
                await stderr
                    .WriteLineAsync("--target aws deploys one environment: name it with --env.")
                    .ConfigureAwait(false);
                return 1;
            case DeployEnvironment.Kubernetes:
                await stderr
                    .WriteLineAsync(
                        "--target kubernetes deploys one environment: name it with --env. 'trax deploy init "
                            + "--target kubernetes' writes one to start from."
                    )
                    .ConfigureAwait(false);
                return 1;
            default:
                return await RunAsync(
                        target ?? DeployEnvironment.Compose,
                        path,
                        outDir,
                        build,
                        exporter,
                        BuildImageAsync,
                        stdout,
                        stderr
                    )
                    .ConfigureAwait(false);
        }
    }

    private static Command CreateInit()
    {
        var app = new Option<string?>("--app")
        {
            Description = "The application's name (default: this directory's, lower-cased).",
        };
        var force = new Option<bool>("--force")
        {
            Description = $"Replace an existing {DeployFile.FileName}.",
        };
        var target = new Option<string>("--target")
        {
            Description =
                "aws also writes a production environment to fill in; kubernetes a staging one, on the try "
                + "profile with Postgres in the cluster.",
            DefaultValueFactory = _ => "compose",
        };
        var region = new Option<string?>("--region")
        {
            Description = "The production environment's region (with --target aws).",
        };
        var domain = new Option<string?>("--domain")
        {
            Description = "The production environment's domain (with --target aws).",
        };
        var init = new Command(
            "init",
            $"Write a first {DeployFile.FileName} from the Trax host projects under this directory."
        )
        {
            app,
            force,
            target,
            region,
            domain,
        };
        init.SetAction(
            (parseResult, _) =>
                InitAsync(
                    Directory.GetCurrentDirectory(),
                    parseResult.GetValue(app),
                    parseResult.GetValue(force),
                    parseResult.GetValue(target) ?? "compose",
                    parseResult.GetValue(region),
                    parseResult.GetValue(domain),
                    parseResult.InvocationConfiguration.Output,
                    parseResult.InvocationConfiguration.Error
                )
        );
        return init;
    }

    internal static async Task<int> InitAsync(
        string directory,
        string? app,
        bool force,
        string target,
        string? region,
        string? domain,
        TextWriter stdout,
        TextWriter stderr
    )
    {
        try
        {
            if (target is not ("compose" or "aws" or "kubernetes"))
                throw new InvalidOperationException(
                    $"--target '{target}' is not one trax deploys to. Use compose, aws or kubernetes."
                );
            var path = Path.Combine(directory, DeployFile.FileName);
            if (File.Exists(path) && !force)
                throw new InvalidOperationException(
                    $"{path} exists. Edit it, or pass --force to write a new one over it."
                );
            var file = DeployInit.Create(directory, app);
            if (string.Equals(target, "aws", StringComparison.Ordinal))
                file.Environments["production"] = new DeployEnvironment
                {
                    Target = DeployEnvironment.Aws,
                    Profile = DeployEnvironment.Production,
                    Region = region ?? "us-east-1",
                    Domain = domain ?? $"{file.App}.example.com",
                    Auth = new DeployAuth(),
                };
            if (string.Equals(target, "kubernetes", StringComparison.Ordinal))
                file.Environments["staging"] = new DeployEnvironment
                {
                    Target = DeployEnvironment.Kubernetes,
                    Profile = DeployEnvironment.Try,
                    Database = new DeployEnvironmentDatabase { InCluster = true },
                };
            await File.WriteAllTextAsync(path, file.Write()).ConfigureAwait(false);
            await stdout.WriteLineAsync($"Wrote {path}:").ConfigureAwait(false);
            foreach (var (name, service) in file.Services)
                await stdout
                    .WriteLineAsync($"  {name, -12}{service.Project}")
                    .ConfigureAwait(false);
            await stdout
                .WriteLineAsync(
                    target switch
                    {
                        "aws" =>
                            "Check the production environment's region, domain and hostedZoneId, name the settings "
                                + "and secrets the hosts read, then run 'trax deploy plan --env production'.",
                        "kubernetes" =>
                            "Name the settings and secrets the hosts read, then run 'trax deploy plan --env staging'.",
                        _ =>
                            "Name the settings and secrets the hosts read, then run 'trax deploy --target compose'.",
                    }
                )
                .ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (Reported(ex))
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    internal static async Task<int> RunAsync(
        string target,
        string filePath,
        string? outDir,
        bool build,
        IHostExporter exporter,
        Func<string, string, Task> buildImage,
        TextWriter stdout,
        TextWriter stderr,
        string? environment = null
    )
    {
        try
        {
            if (!string.Equals(target, "compose", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"--target '{target}' is not one trax deploys to. Use compose, or aws or kubernetes with --env."
                );
            var file = await DeployFile.ReadAsync(filePath).ConfigureAwait(false);
            // Without an environment the stack is for podman, as before environments named their target.
            var env = environment is null ? null : file.Environment(environment);
            var engine = env?.Engine ?? DeployEnvironment.Podman;
            var exports = await ExportAsync(file, filePath, exporter, stdout).ConfigureAwait(false);

            var dir = outDir ?? Path.GetDirectoryName(Path.GetFullPath(filePath)).NotNull();
            await WriteStackAsync(
                    dir,
                    ComposeTarget.Render(
                        file,
                        exports,
                        engine,
                        env?.Limits,
                        env?.DrainTimeoutValue()
                    ),
                    stdout
                )
                .ConfigureAwait(false);

            if (build)
                foreach (var (name, service) in file.Services)
                {
                    await stdout
                        .WriteLineAsync($"Building {ComposeTarget.ImageName(file, name)}...")
                        .ConfigureAwait(false);
                    await buildImage(
                            DeployFile.ProjectPath(filePath, service),
                            ComposeTarget.ImageName(file, name)
                        )
                        .ConfigureAwait(false);
                }

            await stdout
                .WriteLineAsync(
                    $"Copy {ComposeTarget.EnvExampleFile} to .env, fill it in, then in {dir} run:\n  {ComposeRelease(engine)}"
                )
                .ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (Reported(ex))
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>
    /// The failures a deploy command prints as one line and exit 1, rather than a stack trace: what it refuses, and
    /// what the file system, a host's report or a process it starts can throw.
    /// </summary>
    internal static bool Reported(Exception ex) =>
        ex
            is InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or System.Text.Json.JsonException
                or System.ComponentModel.Win32Exception;

    /// <summary>Asks each service's host what it needs, by running it once with --trax-export-deploy.</summary>
    internal static async Task<Dictionary<string, DeployManifest>> ExportAsync(
        DeployFile file,
        string filePath,
        IHostExporter exporter,
        TextWriter stdout
    )
    {
        var placeholders = DotnetHostExporter.Placeholders(file);
        var exports = new Dictionary<string, DeployManifest>(StringComparer.Ordinal);
        foreach (var (name, service) in file.Services)
        {
            await stdout
                .WriteLineAsync($"Asking {name} what it needs ({service.Project})...")
                .ConfigureAwait(false);
            exports[name] = await exporter
                .ExportAsync(DeployFile.ProjectPath(filePath, service), placeholders)
                .ConfigureAwait(false);
        }
        return exports;
    }

    /// <summary>Writes the stack's files into <paramref name="dir"/>, unless a compose file trax did not write is there.</summary>
    private static async Task WriteStackAsync(
        string dir,
        IReadOnlyList<(string Path, string Content)> files,
        TextWriter stdout
    )
    {
        var compose = Path.Combine(dir, ComposeTarget.ComposeFile);
        if (
            File.Exists(compose)
            && !(await FirstLineAsync(compose).ConfigureAwait(false))?.StartsWith(
                ComposeTarget.Header,
                StringComparison.Ordinal
            )
                is true
        )
            throw new InvalidOperationException(
                $"{compose} exists and trax did not write it. Move it aside, or give another --out."
            );
        Directory.CreateDirectory(dir);
        foreach (var (name, content) in files)
        {
            await File.WriteAllTextAsync(Path.Combine(dir, name), content).ConfigureAwait(false);
            await stdout.WriteLineAsync($"Wrote {Path.Combine(dir, name)}").ConfigureAwait(false);
        }
    }

    // The first line of a file, or null when it is empty.
    private static async Task<string?> FirstLineAsync(string path)
    {
        using var reader = new StreamReader(path);
        return await reader.ReadLineAsync().ConfigureAwait(false);
    }

    private static async Task BuildImageAsync(string projectPath, string repository)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(projectPath).NotNull(),
            UseShellExecute = false,
        };
        foreach (
            var arg in new[]
            {
                "publish",
                projectPath,
                "-c",
                "Release",
                "-t:PublishContainer",
                $"-p:ContainerRepository={repository}",
                "-p:ContainerImageTags=latest",
            }
        )
            psi.ArgumentList.Add(arg);
        var (exit, stdout, stderr) = await ChildProcess.RunAsync(psi).ConfigureAwait(false);
        if (exit != 0)
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Building the {repository} image failed (exit {exit}):\n{stdout}{stderr}"
                )
            );
    }
}
