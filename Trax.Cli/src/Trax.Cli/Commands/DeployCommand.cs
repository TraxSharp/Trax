using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using Trax.Cli.Deploy;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Commands;

/// <summary>
/// <c>trax deploy</c>: turns the projects named in <c>trax.deploy.json</c>, and what each host reports it needs
/// (<c>--trax-export-deploy</c>), into a deployment: a compose stack, or an AWS environment
/// (<see cref="DeployAwsCommands"/>). <c>trax deploy init</c> writes a first <c>trax.deploy.json</c>.
/// </summary>
internal static class DeployCommand
{
    public static Command Create()
    {
        var target = new Option<string?>("--target")
        {
            Description =
                "Where to deploy: compose writes a compose stack for any docker or podman host; aws deploys the "
                + "--env environment. Default: aws when --env is given, else compose.",
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
            CreateInit(),
        };
        foreach (var subcommand in DeployAwsCommands.Create())
            command.Subcommands.Add(subcommand);
        command.SetAction(
            async (parseResult, ct) =>
            {
                var stdout = parseResult.InvocationConfiguration.Output;
                var stderr = parseResult.InvocationConfiguration.Error;
                var path =
                    parseResult.GetValue(file)?.FullName
                    ?? Path.Combine(Directory.GetCurrentDirectory(), DeployFile.FileName);
                var environment = parseResult.GetValue(env);
                var chosen =
                    parseResult.GetValue(target) ?? (environment is null ? "compose" : "aws");
                if (!string.Equals(chosen, "aws", StringComparison.Ordinal))
                    return await RunAsync(
                            chosen,
                            path,
                            parseResult.GetValue(output)?.FullName,
                            parseResult.GetValue(build),
                            new DotnetHostExporter(),
                            BuildImageAsync,
                            stdout,
                            stderr
                        )
                        .ConfigureAwait(false);
                if (environment is null)
                {
                    await stderr
                        .WriteLineAsync("--target aws deploys one environment: name it with --env.")
                        .ConfigureAwait(false);
                    return 1;
                }
                return await DeployAwsCommands
                    .DeployAsync(path, environment, new DotnetHostExporter(), stdout, stderr, ct)
                    .ConfigureAwait(false);
            }
        );
        return command;
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
            Description = "aws also writes a production environment to fill in.",
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
            if (target is not ("compose" or "aws"))
                throw new InvalidOperationException(
                    $"--target '{target}' is not one trax deploys to. Use compose or aws."
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
                    Profile = DeployEnvironment.Production,
                    Region = region ?? "us-east-1",
                    Domain = domain ?? $"{file.App}.example.com",
                    Auth = new DeployAuth(),
                };
            await File.WriteAllTextAsync(path, file.Write()).ConfigureAwait(false);
            await stdout.WriteLineAsync($"Wrote {path}:").ConfigureAwait(false);
            foreach (var (name, service) in file.Services)
                await stdout
                    .WriteLineAsync($"  {name, -12}{service.Project}")
                    .ConfigureAwait(false);
            await stdout
                .WriteLineAsync(
                    string.Equals(target, "aws", StringComparison.Ordinal)
                        ? "Check the production environment's region, domain and hostedZoneId, name the settings and "
                            + "secrets the hosts read, then run 'trax deploy plan --env production'."
                        : "Name the settings and secrets the hosts read, then run 'trax deploy --target compose'."
                )
                .ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
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
        TextWriter stderr
    )
    {
        try
        {
            if (!string.Equals(target, "compose", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"--target '{target}' is not one trax deploys to. Use compose, or aws with --env."
                );
            var file = await DeployFile.ReadAsync(filePath).ConfigureAwait(false);
            var exports = await ExportAsync(file, filePath, exporter, stdout).ConfigureAwait(false);

            var dir = outDir ?? Path.GetDirectoryName(Path.GetFullPath(filePath)).NotNull();
            var files = ComposeTarget.Render(file, exports);
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
                await File.WriteAllTextAsync(Path.Combine(dir, name), content)
                    .ConfigureAwait(false);
                await stdout
                    .WriteLineAsync($"Wrote {Path.Combine(dir, name)}")
                    .ConfigureAwait(false);
            }

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
                    $"Copy {ComposeTarget.EnvExampleFile} to .env, fill it in, then run 'docker compose up -d' "
                        + $"(or 'podman compose up -d') in {dir}."
                )
                .ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }

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
