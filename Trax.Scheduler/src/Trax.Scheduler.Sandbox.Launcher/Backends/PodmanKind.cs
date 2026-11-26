namespace Trax.Scheduler.Sandbox.Launcher.Backends;

/// <summary>What <see cref="PodmanApi.ListAsync(PodmanKind, string, CancellationToken)"/> lists.</summary>
internal enum PodmanKind
{
    Container,
    Volume,
    Pod,
}
