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

    /// <summary>
    /// What a release must restart, given the files an earlier deploy wrote (by file name) and whether the Secrets
    /// changed: a unit whose own <c>.kube</c> or pod YAML changed, the unit that runs a changed migration, and every
    /// unit when the network or the Secrets changed (each unit plays the Secrets before its pod). A unit whose files
    /// are as they were keeps running, so a release that changes only the app's images leaves Postgres and RabbitMQ
    /// up. With no earlier files every unit starts. <see cref="Release.Removed"/> lists the units an earlier deploy
    /// wrote that this one does not, to stop and remove.
    /// </summary>
    public Release ReleaseFrom(IReadOnlyDictionary<string, string> previous, bool secretsChanged)
    {
        string? Before(string file) => previous.GetValueOrDefault(file);
        var now = Files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);
        bool Changed(string file) =>
            !string.Equals(Before(file), now.GetValueOrDefault(file), StringComparison.Ordinal);
        var removed = previous
            .Keys.Where(f => f.EndsWith(".kube", StringComparison.Ordinal))
            .Select(f => f[..^".kube".Length])
            .Where(u => !Units.Contains(u, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (previous.Count == 0 || secretsChanged || Changed($"{Name}.network"))
            return new Release(Units, removed);
        var migration = $"{Name}-migrate.yaml";
        var restart = Units
            .Where(u =>
                Changed($"{u}.kube")
                || Changed($"{u}.yaml")
                || (
                    Changed(migration)
                    && now.GetValueOrDefault($"{u}.kube")
                        ?.Contains(migration, StringComparison.Ordinal)
                        is true
                )
            )
            .ToList();
        return new Release(restart, removed);
    }

    /// <summary>The units a release restarts, in start order, and those it stops and removes.</summary>
    internal sealed record Release(IReadOnlyList<string> Restart, IReadOnlyList<string> Removed);
}
