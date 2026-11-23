namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>Builds a service's image, and pushes it when the environment names a registry.</summary>
internal interface IContainerImagePublisher
{
    /// <summary>
    /// Publishes <paramref name="projectPath"/> as <c>[&lt;registry&gt;/]&lt;repository&gt;:&lt;tag&gt;</c>: to
    /// <paramref name="registry"/> when there is one, else to the local container runtime, which
    /// <paramref name="localRuntime"/> names when it must be podman.
    /// </summary>
    Task PublishAsync(
        string projectPath,
        string? registry,
        string repository,
        string tag,
        string? localRuntime = null
    );
}
