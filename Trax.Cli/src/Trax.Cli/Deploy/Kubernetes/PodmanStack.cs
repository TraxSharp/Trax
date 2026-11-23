using k8s.Models;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// A kubernetes environment rendered for one host: the files written beside the project (pod YAML and Quadlet units,
/// no secret value in any of them), the Secrets the pods read, which a deploy fills from <c>.env</c>, and the units
/// to start, the service units last.
/// </summary>
internal sealed record PodmanStack(
    string Name,
    IReadOnlyList<(string Path, string Content)> Files,
    IReadOnlyList<V1Secret> Secrets,
    IReadOnlyList<string> Units
)
{
    /// <summary>Every key the Secrets must hold, as <c>.env</c> names it.</summary>
    public IEnumerable<string> SecretKeys => Secrets.SelectMany(s => s.StringData.Keys);
}
