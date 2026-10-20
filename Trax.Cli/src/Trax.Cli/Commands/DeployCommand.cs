using System.CommandLine;
using System.Diagnostics;
using Trax.Cli.Deploy;
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
            (parseResult, ct) =>
            {
                var path =
                    parseResult.GetValue(file)?.FullName
                    ?? Path.Combine(Directory.GetCurrentDirectory(), DeployFile.FileName);
                var environment = parseResult.GetValue(env);
                var chosen =
                    parseResult.GetValue(target) ?? (environment is null ? "compose" : "aws");
                if (chosen != "aws")
                    return Task.FromResult(
                        Run(
                            chosen,
                            path,
                            parseResult.GetValue(output)?.FullName,
                            parseResult.GetValue(build),
                            new DotnetHostExporter(),
                            BuildImage
                        )
                    );
                if (environment is null)
                {
                    Console.Error.WriteLine(
                        "--target aws deploys one environment: name it with --env."
                    );
                    return Task.FromResult(1);
                }
                return DeployAwsCommands.DeployAsync(
                    path,
                    environment,
                    new DotnetHostExporter(),
                    ct
                );
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
            if (target == "aws")
                file.Environments["production"] = new DeployEnvironment
                {
                    Profile = DeployEnvironment.Production,
                    Region = region ?? "us-east-1",
                    Domain = domain ?? $"{file.App}.example.com",
                    Auth = new DeployAuth(),
                };
            File.WriteAllText(path, file.Write());
            Console.WriteLine($"Wrote {path}:");
            foreach (var (name, service) in file.Services)
                Console.WriteLine($"  {name, -12}{service.Project}");
            Console.WriteLine(
                target == "aws"
                    ? "Check the production environment's region, domain and hostedZoneId, name the settings and "
                        + "secrets the hosts read, then run 'trax deploy plan --env production'."
                    : "Name the settings and secrets the hosts read, then run 'trax deploy --target compose'."
            );
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
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
        Action<string, string> buildImage
    )
    {
        try
        {
            if (target != "compose")
                throw new InvalidOperationException(
                    $"--target '{target}' is not one trax deploys to. Use compose, or aws with --env."
                );
            var file = DeployFile.Read(filePath);
            var exports = Export(file, filePath, exporter);

            var dir = outDir ?? Path.GetDirectoryName(Path.GetFullPath(filePath))!;
            var files = ComposeTarget.Render(file, exports);
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
                $"Copy {ComposeTarget.EnvExampleFile} to .env, fill it in, then run 'docker compose up -d' "
                    + $"(or 'podman compose up -d') in {dir}."
            );
            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

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
