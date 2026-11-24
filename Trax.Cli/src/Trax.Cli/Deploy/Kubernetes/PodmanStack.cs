using k8s.Models;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// A kubernetes environment rendered for one host: the files written beside the project (pod YAML and Quadlet units,
/// no secret value in any of them), the Secrets the pods read, which a deploy fills from <c>.env</c>, the units to
/// start, the service units last, which of them run the app's own hosts, and where the filled Secrets are written.
/// <see cref="ProxySecretName"/> names the Secret of a tailnet exposure's proxy secret, which a deploy mints and writes
/// to a file of its own.
/// </summary>
internal sealed record PodmanStack(
    string Name,
    IReadOnlyList<(string Path, string Content)> Files,
    IReadOnlyList<V1Secret> Secrets,
    IReadOnlyList<string> Units,
    IReadOnlyList<string> ServiceUnits,
    string SecretPath,
    string? ProxySecretName = null
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
    /// wrote that this one does not, to stop and remove. <see cref="Release.Drain"/> says the hosts are drained before
    /// the restart: an earlier deploy's units run, and the release restarts one of the app's own.
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
        if (previous.Count == 0)
            return new Release(Units, removed, Drain: false);
        if (secretsChanged || Changed($"{Name}.network"))
            return new Release(Units, removed, Drain: ServiceUnits.Count > 0);
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
        return new Release(
            restart,
            removed,
            Drain: restart.Exists(u => ServiceUnits.Contains(u, StringComparer.Ordinal))
        );
    }

    /// <summary>
    /// The units a release restarts, in start order, those it stops and removes, and whether it drains the hosts
    /// before the restart and ends the drain after.
    /// </summary>
    internal sealed record Release(
        IReadOnlyList<string> Restart,
        IReadOnlyList<string> Removed,
        bool Drain
    );
}
