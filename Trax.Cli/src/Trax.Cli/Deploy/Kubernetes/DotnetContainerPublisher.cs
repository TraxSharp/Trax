using System.Diagnostics;
using System.Globalization;
using Trax.Core.Extensions;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// Builds a service's image with <c>dotnet publish -t:PublishContainer</c> for linux-x64: pushed to the registry when
/// one is named, with whatever login docker or podman holds for it, else loaded into the local container runtime.
/// </summary>
internal sealed class DotnetContainerPublisher : IContainerImagePublisher
{
    public async Task PublishAsync(
        string projectPath,
        string? registry,
        string repository,
        string tag
    )
    {
        var (exit, stdout, stderr) = await ChildProcess
            .RunAsync(StartInfo(projectPath, registry, repository, tag))
            .ConfigureAwait(false);
        if (exit != 0)
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Publishing {Path.GetFileName(projectPath)} as {repository}:{tag} failed (exit {exit}):\n{stdout}{stderr}"
                )
            );
    }

    internal static ProcessStartInfo StartInfo(
        string projectPath,
        string? registry,
        string repository,
        string tag
    )
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(projectPath).NotNull(),
            UseShellExecute = false,
        };
        string[] args =
        [
            "publish",
            projectPath,
            "-c",
            "Release",
            "--os",
            "linux",
            "--arch",
            "x64",
            "-t:PublishContainer",
            $"-p:ContainerImageTags={tag}",
        ];
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        if (registry is null)
            psi.ArgumentList.Add($"-p:ContainerRepository={repository}");
        else
        {
            // ghcr.io/acme is the registry ghcr.io and the repository acme/<image>.
            var slash = registry.IndexOf('/', StringComparison.Ordinal);
            psi.ArgumentList.Add(
                $"-p:ContainerRegistry={(slash < 0 ? registry : registry[..slash])}"
            );
            psi.ArgumentList.Add(
                $"-p:ContainerRepository={(slash < 0 ? "" : registry[(slash + 1)..] + "/")}{repository}"
            );
        }
        return psi;
    }
}
