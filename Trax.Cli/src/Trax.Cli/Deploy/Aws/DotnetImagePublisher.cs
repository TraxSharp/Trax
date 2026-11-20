using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy.Aws;

/// <summary>
/// Builds a service's image with <c>dotnet publish -t:PublishContainer</c> for linux-x64 and pushes it straight to
/// the registry, with the registry login passed in the environment: no docker or podman needed.
/// </summary>
internal sealed class DotnetImagePublisher : IImagePublisher
{
    public void Publish(string projectPath, string repositoryUri, string tag, RegistryLogin login)
    {
        var (exit, stdout, stderr) = ChildProcess.Run(
            StartInfo(projectPath, repositoryUri, tag, login)
        );
        if (exit != 0)
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Publishing {Path.GetFileName(projectPath)} to {repositoryUri} failed (exit {exit}):\n{stdout}{stderr}"
                )
            );
    }

    internal static ProcessStartInfo StartInfo(
        string projectPath,
        string repositoryUri,
        string tag,
        RegistryLogin login
    )
    {
        var slash = repositoryUri.IndexOf('/', StringComparison.Ordinal);
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            UseShellExecute = false,
        };
        foreach (
            var arg in new[]
            {
                "publish",
                projectPath,
                "-c",
                "Release",
                "--os",
                "linux",
                "--arch",
                "x64",
                "-t:PublishContainer",
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
