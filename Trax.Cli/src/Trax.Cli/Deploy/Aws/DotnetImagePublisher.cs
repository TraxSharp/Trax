using System.Diagnostics;
using System.Text.Json.Nodes;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy.Aws;

/// <summary>
/// Builds a service's image with <c>dotnet publish -t:PublishContainer</c> for linux-x64, from a locked restore
/// (<see cref="LockedPublish"/>), and pushes it straight to the registry, with the registry login passed in the
/// environment: no docker or podman needed.
/// </summary>
internal sealed class DotnetImagePublisher : IImagePublisher
{
    public Task PublishAsync(
        string projectPath,
        string repositoryUri,
        string tag,
        RegistryLogin login
    ) =>
        LockedPublish.RunAsync(
            LockedPublish.Restore(projectPath),
            StartInfo(projectPath, repositoryUri, tag, login),
            repositoryUri
        );

    internal static ProcessStartInfo StartInfo(
        string projectPath,
        string repositoryUri,
        string tag,
        RegistryLogin login
    )
    {
        var slash = repositoryUri.IndexOf('/', StringComparison.Ordinal);
        var psi = LockedPublish.Publish(projectPath);
        foreach (
            var arg in new[]
            {
                "-t:PublishContainer",
                $"-p:ContainerRuntimeIdentifier={LockedPublish.Runtime}",
                $"-p:ContainerRegistry={repositoryUri[..slash]}",
                $"-p:ContainerRepository={repositoryUri[(slash + 1)..]}",
                $"-p:ContainerImageTags={tag}",
            }
        )
            psi.ArgumentList.Add(arg);
        psi.Environment["DOTNET_CONTAINER_REGISTRY_UNAME"] = login.User;
        psi.Environment["DOTNET_CONTAINER_REGISTRY_PWORD"] = login.Password;
        return psi;
    }
}
