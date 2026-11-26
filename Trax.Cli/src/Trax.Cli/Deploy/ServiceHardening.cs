namespace Trax.Cli.Deploy;

/// <summary>
/// The limits one service runs under, the same on every target that has them: every Linux capability dropped and
/// only <see cref="AddCapabilities"/> given back, no privilege gained after start, at most <see cref="Pids"/>
/// processes where the runtime counts them, at most <see cref="MemoryMiB"/> of memory and <see cref="Cpus"/> of CPU
/// when they are set, a root filesystem it cannot write when <see cref="ReadOnlyRoot"/>, and a user namespace of its
/// own where the runtime has one.
/// </summary>
/// <param name="AddCapabilities">The Linux capabilities given back after every one is dropped.</param>
/// <param name="MemoryMiB">The memory each copy may use, in MiB; null leaves it unlimited.</param>
/// <param name="Pids">The processes and threads one copy may run; null leaves it to the runtime.</param>
/// <param name="Cpus">The CPUs each copy may use (1.5 is one and a half cores); null leaves it unlimited.</param>
/// <param name="ReadOnlyRoot">Whether the root filesystem is read-only, with a scratch <c>/tmp</c> of its own.</param>
internal sealed record ServiceHardening(
    IReadOnlyList<string> AddCapabilities,
    int? MemoryMiB,
    int? Pids,
    decimal? Cpus,
    bool ReadOnlyRoot
)
{
    /// <summary>The processes and threads one copy may run when its entry names none, where the runtime counts them.</summary>
    public const int DefaultPids = 512;

    /// <summary>The least memory a service may name, in MiB: below it a .NET host does not start.</summary>
    public const int MinimumMemoryMiB = 128;

    /// <summary>The fewest processes and threads a service may name: a .NET host starts about thirty threads.</summary>
    public const int MinimumPids = 64;

    /// <summary>The least CPU a service may name: a tenth of a core.</summary>
    public const decimal MinimumCpus = 0.1m;

    /// <summary>The most CPU a service may name, the largest Fargate task.</summary>
    public const decimal MaximumCpus = 16m;

    /// <summary>Postgres's memory when the environment names none, in MiB.</summary>
    public const int DatabaseMemoryMiB = 1024;

    /// <summary>RabbitMQ's memory when the environment names none, in MiB.</summary>
    public const int BrokerMemoryMiB = 512;

    /// <summary>
    /// Postgres's entrypoint takes ownership of its data directory and drops to the postgres user, so it needs these
    /// five back (measured on postgres:17 under rootless podman 6.1.4, 2026-11-17). Its root stays writable: its
    /// entrypoint writes outside its data volume, and that has not been measured read-only.
    /// </summary>
    public static ServiceHardening Database(DeployLimits? limits) =>
        new(
            ["CHOWN", "DAC_OVERRIDE", "FOWNER", "SETGID", "SETUID"],
            limits?.Memory ?? DatabaseMemoryMiB,
            limits?.Pids ?? DefaultPids,
            limits?.Cpus,
            ReadOnlyRoot: false
        );

    /// <summary>
    /// RabbitMQ's entrypoint drops to the rabbitmq user and reads its Erlang cookie, so it needs these four back
    /// (measured on rabbitmq:4 under rootless podman 6.1.4, 2026-11-17: without DAC_OVERRIDE, or without both CHOWN
    /// and FOWNER, the cookie read fails with eacces). Its root stays writable, as Postgres's does.
    /// </summary>
    public static ServiceHardening Broker(DeployLimits? limits) =>
        new(
            ["CHOWN", "DAC_OVERRIDE", "SETGID", "SETUID"],
            limits?.Memory ?? BrokerMemoryMiB,
            limits?.Pids ?? DefaultPids,
            limits?.Cpus,
            ReadOnlyRoot: false
        );

    /// <summary>The uid and gid of the rabbitmq image's own user.</summary>
    public const long BrokerUser = 999;

    /// <summary>
    /// RabbitMQ on Kubernetes and under <c>podman kube play</c>, run as the image's own user (<see cref="BrokerUser"/>)
    /// from the start, so it needs no capability back and its entrypoint changes no owner. Started as root, the
    /// entrypoint drops to that user before the server writes its Erlang cookie, while the readiness probe
    /// (<c>rabbitmq-diagnostics</c>, run as the container's user, root) writes one first, which the server then cannot
    /// read: it stops with eacces and restarts for ever (measured on rabbitmq:4 under k3s v1.36.6 with
    /// <c>hostUsers: false</c>, 2026-11-24). Its root stays writable, as Postgres's does.
    /// </summary>
    public static ServiceHardening BrokerAsItsUser(DeployLimits? limits) =>
        new(
            [],
            limits?.Memory ?? BrokerMemoryMiB,
            limits?.Pids ?? DefaultPids,
            limits?.Cpus,
            ReadOnlyRoot: false
        );

    /// <summary>
    /// A Trax service needs no Linux capability and writes nothing outside <c>/tmp</c>. Its memory and CPU are what
    /// its entry names, unlimited when it names none.
    /// </summary>
    public static ServiceHardening Trax(DeployService service) =>
        new([], service.Memory, service.Pids ?? DefaultPids, service.Cpus, ReadOnlyRoot: true);
}
