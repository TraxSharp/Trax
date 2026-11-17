namespace Trax.Cli.Deploy;

/// <summary>
/// The limits one service runs under, the same on every target that has them: every Linux capability dropped and
/// only <see cref="AddCapabilities"/> given back, no privilege gained after start, at most <see cref="Pids"/>
/// processes, at most <see cref="MemoryMiB"/> of memory, and a user namespace of its own where the runtime has one.
/// </summary>
internal sealed record ServiceHardening(IReadOnlyList<string> AddCapabilities, int MemoryMiB)
{
    /// <summary>Processes and threads one copy may run.</summary>
    public const int Pids = 512;

    /// <summary>A Trax service's memory when its entry in trax.deploy.json names none, in MiB.</summary>
    public const int DefaultMemoryMiB = 1024;

    /// <summary>The least memory a service may name, in MiB: below it a .NET host does not start.</summary>
    public const int MinimumMemoryMiB = 128;

    /// <summary>
    /// Postgres's entrypoint takes ownership of its data directory and drops to the postgres user, so it needs these
    /// five back (measured on postgres:17 under rootless podman 6.1.4, 2026-11-17).
    /// </summary>
    public static readonly ServiceHardening Database = new(
        ["CHOWN", "DAC_OVERRIDE", "FOWNER", "SETGID", "SETUID"],
        1024
    );

    /// <summary>
    /// RabbitMQ's entrypoint drops to the rabbitmq user and reads its Erlang cookie, so it needs these four back
    /// (measured on rabbitmq:4 under rootless podman 6.1.4, 2026-11-17: without DAC_OVERRIDE, or without both CHOWN
    /// and FOWNER, the cookie read fails with eacces).
    /// </summary>
    public static readonly ServiceHardening Broker = new(
        ["CHOWN", "DAC_OVERRIDE", "SETGID", "SETUID"],
        512
    );

    /// <summary>A Trax service needs no Linux capability and runs in the memory its entry names.</summary>
    public static ServiceHardening Trax(DeployService service) =>
        new([], service.Memory ?? DefaultMemoryMiB);
}
