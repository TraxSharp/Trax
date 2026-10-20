using System.CommandLine;
using System.Diagnostics;
using Trax.Cli.Deploy;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Commands;

/// <summary>
/// <c>trax deploy</c>: turns the projects named in <c>trax.deploy.json</c>, and what each host reports it needs
/// (<c>--trax-export-deploy</c>), into a deployment. <c>trax deploy init</c> writes a first <c>trax.deploy.json</c>.
/// </summary>
internal static class DeployCommand
{
    public static Command Create()
    {
        var target = new Option<string>("--target")
        {
            Description =
                "Where to deploy. compose writes a compose stack for any docker or podman host.",
            DefaultValueFactory = _ => "compose",
        };
        var file = new Option<FileInfo?>("--file")
        {
            Description =
                $"The deployment's {DeployFile.FileName} (default: the one in this directory).",
        };
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
            file,
            output,
            build,
            CreateInit(),
        };
        command.SetAction(parseResult =>
        {
            var path =
                parseResult.GetValue(file)?.FullName
                ?? Path.Combine(Directory.GetCurrentDirectory(), DeployFile.FileName);
            return Run(
                parseResult.GetValue(target) ?? "compose",
                path,
                parseResult.GetValue(output)?.FullName,
                parseResult.GetValue(build),
                new DotnetHostExporter(),
                BuildImage
            );
        });
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
        var init = new Command(
            "init",
            $"Write a first {DeployFile.FileName} from the Trax host projects under this directory."
        )
        {
            app,
            force,
        };
        init.SetAction(parseResult =>
            Init(
                Directory.GetCurrentDirectory(),
                parseResult.GetValue(app),
                parseResult.GetValue(force)
            )
        );
        return init;
    }

    internal static int Init(string directory, string? app, bool force)
    {
        try
        {
            var path = Path.Combine(directory, DeployFile.FileName);
            if (File.Exists(path) && !force)
                throw new InvalidOperationException(
                    $"{path} exists. Edit it, or pass --force to write a new one over it."
                );
            var file = DeployInit.Create(directory, app);
            File.WriteAllText(path, file.Write());
            Console.WriteLine($"Wrote {path}:");
            foreach (var (name, service) in file.Services)
                Console.WriteLine($"  {name, -12}{service.Project}");
            Console.WriteLine(
                "Name the settings and secrets the hosts read, then run 'trax deploy --target compose'."
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
                    $"--target '{target}' is not one trax deploys to yet. Use compose."
                );
            var file = DeployFile.Read(filePath);
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
