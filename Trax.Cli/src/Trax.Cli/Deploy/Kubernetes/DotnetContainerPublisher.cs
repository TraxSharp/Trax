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
        string tag,
        string? localRuntime = null
    )
    {
        var (exit, stdout, stderr) = await ChildProcess
            .RunAsync(StartInfo(projectPath, registry, repository, tag, localRuntime))
            .ConfigureAwait(false);
        if (exit != 0)
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Publishing {Path.GetFileName(projectPath)} as {repository}:{tag} failed (exit {exit}):\n{stdout}{stderr}"
                )
            );
    }

    /// <summary>
    /// The SDK's name for a local container runtime. It accepts only <c>Docker</c> and <c>Podman</c>, case and all
    /// (CONTAINER2002 otherwise), where trax.deploy.json writes them in lower case.
    /// </summary>
    internal static string LocalRegistry(string runtime) =>
        runtime switch
        {
            DeployEnvironment.Podman => "Podman",
            DeployEnvironment.Docker => "Docker",
            _ => throw new InvalidOperationException(
                $"'{runtime}' is not a container runtime the .NET SDK loads images into. Use podman or docker."
            ),
        };

    internal static ProcessStartInfo StartInfo(
        string projectPath,
        string? registry,
        string repository,
        string tag,
        string? localRuntime = null
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
        {
            psi.ArgumentList.Add($"-p:ContainerRepository={repository}");
            // The SDK loads into docker when it finds one; a podman environment's units read podman's store.
            if (localRuntime is not null)
                psi.ArgumentList.Add($"-p:LocalRegistry={LocalRegistry(localRuntime)}");
        }
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
