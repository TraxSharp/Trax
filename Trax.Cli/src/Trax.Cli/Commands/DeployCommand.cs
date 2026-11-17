using System.CommandLine;
using System.Diagnostics;
using Trax.Cli.Deploy;
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
            (parseResult, ct) =>
                Dispatch(
                    parseResult.GetValue(file)?.FullName
                        ?? Path.Combine(Directory.GetCurrentDirectory(), DeployFile.FileName),
                    parseResult.GetValue(target),
                    parseResult.GetValue(env),
                    parseResult.GetValue(output)?.FullName,
                    parseResult.GetValue(build),
                    new DotnetHostExporter(ct),
                    ct
                )
        );
        return command;
    }

    /// <summary>
    /// Picks the target: with <paramref name="environment"/>, the environment's own, which a <paramref name="target"/>
    /// given beside it must match; without, <paramref name="target"/> or compose.
    /// </summary>
    internal static Task<int> Dispatch(
        string path,
        string? target,
        string? environment,
        string? outDir,
        bool build,
        IHostExporter exporter,
        CancellationToken ct
    )
    {
        if (environment is null)
        {
            switch (target ?? DeployEnvironment.Compose)
            {
                case DeployEnvironment.Aws:
                    Console.Error.WriteLine(
                        "--target aws deploys one environment: name it with --env."
                    );
                    return Task.FromResult(1);
                case DeployEnvironment.Kubernetes:
                    Console.Error.WriteLine(
                        "--target kubernetes deploys one environment: name it with --env. 'trax deploy init "
                            + "--target kubernetes' writes one to start from."
                    );
                    return Task.FromResult(1);
                default:
                    return Task.FromResult(
                        Run(
                            target ?? DeployEnvironment.Compose,
                            path,
                            outDir,
                            build,
                            exporter,
                            BuildImage
                        )
                    );
            }
        }
        string chosen;
        try
        {
            var file = DeployFile.Read(path);
            chosen = file.Environment(environment).Target ?? "";
            if (target is not null && target != chosen)
                throw new InvalidOperationException(
                    $"--target {target} does not match environment '{environment}', whose target is {chosen}. "
                        + "Leave --target out, or change the environment's target."
                );
        }
        catch (Exception ex) when (Reported(ex))
        {
            Console.Error.WriteLine(ex.Message);
            return Task.FromResult(1);
        }
        return chosen switch
        {
            DeployEnvironment.Aws => DeployAwsCommands.DeployAsync(path, environment, exporter, ct),
            DeployEnvironment.Kubernetes => DeployKubernetesCommands.DeployAsync(
                path,
                environment,
                exporter,
                ct
            ),
            _ => Task.FromResult(
                Run(
                    DeployEnvironment.Compose,
                    path,
                    outDir,
                    build,
                    exporter,
                    BuildImage,
                    environment
                )
            ),
        };
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
        init.SetAction(parseResult =>
            Init(
                Directory.GetCurrentDirectory(),
                parseResult.GetValue(app),
                parseResult.GetValue(force),
                parseResult.GetValue(target) ?? "compose",
                parseResult.GetValue(region),
                parseResult.GetValue(domain)
            )
        );
        return init;
    }

    internal static int Init(
        string directory,
        string? app,
        bool force,
        string target = "compose",
        string? region = null,
        string? domain = null
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
            if (target == "aws")
                file.Environments["production"] = new DeployEnvironment
                {
                    Target = DeployEnvironment.Aws,
                    Profile = DeployEnvironment.Production,
                    Region = region ?? "us-east-1",
                    Domain = domain ?? $"{file.App}.example.com",
                    Auth = new DeployAuth(),
                };
            if (target == "kubernetes")
                file.Environments["staging"] = new DeployEnvironment
                {
                    Target = DeployEnvironment.Kubernetes,
                    Profile = DeployEnvironment.Try,
                    Database = new DeployEnvironmentDatabase { InCluster = true },
                };
            File.WriteAllText(path, file.Write());
            Console.WriteLine($"Wrote {path}:");
            foreach (var (name, service) in file.Services)
                Console.WriteLine($"  {name, -12}{service.Project}");
            Console.WriteLine(
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
            );
            return 0;
        }
        catch (Exception ex) when (Reported(ex))
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    internal static int Run(
        string target,
        string filePath,
        string? outDir,
        bool build,
        IHostExporter exporter,
        Action<string, string> buildImage,
        string? environment = null
    )
    {
        try
        {
            if (target != "compose")
                throw new InvalidOperationException(
                    $"--target '{target}' is not one trax deploys to. Use compose, or aws or kubernetes with --env."
                );
            var file = DeployFile.Read(filePath);
            // Without an environment the stack is for podman, as before environments named their target.
            var engine = environment is null
                ? DeployEnvironment.Podman
                : file.Environment(environment).Engine ?? DeployEnvironment.Podman;
            var exports = Export(file, filePath, exporter);

            var dir = outDir ?? Path.GetDirectoryName(Path.GetFullPath(filePath))!;
            var files = ComposeTarget.Render(file, exports, engine);
            var compose = Path.Combine(dir, ComposeTarget.ComposeFile);
            if (
                File.Exists(compose)
                && !File.ReadLines(compose)
                    .FirstOrDefault()
                    ?.StartsWith(ComposeTarget.Header, StringComparison.Ordinal)
                    is true
            )
                throw new InvalidOperationException(
                    $"{compose} exists and trax did not write it. Move it aside, or give another --out."
                );
            Directory.CreateDirectory(dir);
            foreach (var (name, content) in files)
            {
                File.WriteAllText(Path.Combine(dir, name), content);
                Console.WriteLine($"Wrote {Path.Combine(dir, name)}");
            }

            if (build)
                foreach (var (name, service) in file.Services)
                {
                    Console.WriteLine($"Building {ComposeTarget.ImageName(file, name)}...");
                    buildImage(
                        DeployFile.ProjectPath(filePath, service),
                        ComposeTarget.ImageName(file, name)
                    );
                }

            Console.WriteLine(
                $"Copy {ComposeTarget.EnvExampleFile} to .env, fill it in, then run '{engine} compose up -d' in {dir}."
            );
            return 0;
        }
        catch (Exception ex) when (Reported(ex))
        {
            Console.Error.WriteLine(ex.Message);
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
    internal static Dictionary<string, DeployManifest> Export(
        DeployFile file,
        string filePath,
        IHostExporter exporter
    )
    {
        var placeholders = DotnetHostExporter.Placeholders(file);
        var exports = new Dictionary<string, DeployManifest>(StringComparer.Ordinal);
        foreach (var (name, service) in file.Services)
        {
            Console.WriteLine($"Asking {name} what it needs ({service.Project})...");
            exports[name] = exporter.Export(
                DeployFile.ProjectPath(filePath, service),
                placeholders
            );
        }
        return exports;
    }

    private static void BuildImage(string projectPath, string repository)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
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
        var (exit, stdout, stderr) = ChildProcess.Run(psi);
        if (exit != 0)
            throw new InvalidOperationException(
                $"Building the {repository} image failed (exit {exit}):\n{stdout}{stderr}"
            );
    }
}
