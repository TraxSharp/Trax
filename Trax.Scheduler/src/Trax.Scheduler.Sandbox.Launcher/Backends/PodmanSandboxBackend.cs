using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Trax.Scheduler.Sandbox.Launcher.Configuration;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Launcher.Backends;

/// <summary>
/// Runs a sandbox under rootless podman on one host, through the API socket only the launcher holds.
/// </summary>
/// <remarks>
/// <para>
/// Per run: an input and an output volume, both tmpfs with a size, so the output is capped by the
/// kernel rather than by a check; a holder, a launcher-owned container of a pinned pause image
/// that keeps both volumes mounted (a tmpfs volume is unmounted, and its contents lost, when the
/// last container using it stops); and the sandbox, under the configured runtime, with no network
/// namespace but its own loopback, a read-only root, every capability dropped, no new privileges,
/// a memory, CPU and pids limit, and a tmpfs scratch directory.
/// </para>
/// <para>
/// The input goes into the holder's input mount through the API before the sandbox starts. After
/// the sandbox exits, or is killed, the launcher reads the output volume as a tar through the
/// holder, which never ran anything from the sandbox, and checks it entry by entry. Then every
/// container and volume of the run is removed. See scheduler/0021.
/// </para>
/// </remarks>
internal sealed class PodmanSandboxBackend(
    PodmanApi api,
    SandboxLauncherOptions options,
    string ociRuntime,
    TimeProvider time,
    ILogger<PodmanSandboxBackend> logger
) : ISandboxBackend
{
    internal const string LauncherLabel = "trax.sandbox.launcher";
    internal const string RunLabel = "trax.sandbox.run";
    private const long MiB = 1024 * 1024;

    /// <summary>The podman runtime name for each <see cref="SandboxRuntime"/>, as containers.conf names them.</summary>
    internal static string RuntimeName(SandboxRuntime runtime) =>
        runtime switch
        {
            SandboxRuntime.Kata => "kata",
            SandboxRuntime.GVisor => "runsc",
            _ => throw new ArgumentOutOfRangeException(nameof(runtime)),
        };

    public IReadOnlyList<string> CanaryProbes => [];

    public bool LoopbackOnly => true;

    public async Task<SandboxExecution> RunAsync(SandboxJob job, CancellationToken ct)
    {
        var names = new PodmanRunNames(job.RunId);
        try
        {
            var holder = await StartHolderAsync(job, names, ct).ConfigureAwait(false);
            var input = await SandboxArchive
                .WriteAsync(job.Input.Select(f => new SandboxFile(f.Key, f.Value)).ToList(), ct)
                .ConfigureAwait(false);
            await api.PutArchiveAsync(holder, "/in", input, ct).ConfigureAwait(false);

            var sandbox = await api.CreateContainerAsync(SandboxSpec(job, names), ct)
                .ConfigureAwait(false);
            await api.StartAsync(sandbox, ct).ConfigureAwait(false);
            var (end, code) = await WaitAsync(sandbox, job.Limits, ct).ConfigureAwait(false);

            var (files, refused) = await CollectAsync(holder, ct).ConfigureAwait(false);
            return new SandboxExecution(end, code, files, refused);
        }
        catch (Exception ex)
            when (ex
                    is PodmanApiException
                        or HttpRequestException
                        or IOException
                        or FormatException
                        or System.Text.Json.JsonException
            )
        {
            logger.LogError(
                ex,
                "Sandbox run {RunId} could not be started or collected.",
                job.RunId
            );
            return SandboxExecution.Failure(ex.Message);
        }
        finally
        {
            await RemoveAsync(names).ConfigureAwait(false);
        }
    }

    private async Task<string> StartHolderAsync(
        SandboxJob job,
        PodmanRunNames names,
        CancellationToken ct
    )
    {
        var inputBytes = job.Input.Values.Sum(v => (long)v.Length);
        await api.CreateVolumeAsync(names.Input, Tmpfs(inputBytes + MiB, "0755"), Labels(job), ct)
            .ConfigureAwait(false);
        await api.CreateVolumeAsync(
                names.Output,
                Tmpfs(job.Limits.DiskMiB * MiB, "0777"),
                Labels(job),
                ct
            )
            .ConfigureAwait(false);

        var holder = await api.CreateContainerAsync(HolderSpec(job, names), ct)
            .ConfigureAwait(false);
        await api.StartAsync(holder, ct).ConfigureAwait(false);
        return holder;
    }

    /// <summary>Waits for the sandbox to exit, killing it at its timeout.</summary>
    private async Task<(SandboxEnd, int?)> WaitAsync(
        string sandbox,
        SandboxLimits limits,
        CancellationToken ct
    )
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(limits.TimeoutSeconds),
            time
        );
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            var code = await api.WaitAsync(sandbox, linked.Token).ConfigureAwait(false);
            var state = (await api.InspectAsync(sandbox, ct).ConfigureAwait(false))?["State"];
            var oom = state?["OOMKilled"]?.GetValue<bool>() ?? false;
            return (oom ? SandboxEnd.OutOfMemory : SandboxEnd.Exited, code);
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            await api.KillAsync(sandbox, ct).ConfigureAwait(false);
            await api.WaitAsync(sandbox, ct).ConfigureAwait(false);
            return (SandboxEnd.TimedOut, null);
        }
    }

    private async Task<(IReadOnlyList<SandboxFile>, string?)> CollectAsync(
        string holder,
        CancellationToken ct
    )
    {
        using var response = await api.GetArchiveAsync(holder, "/out", ct).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
            return await SandboxArchive
                .ReadAsync(stream, OutputLimits(options.Caps), "out/", ct)
                .ConfigureAwait(false);
    }

    /// <summary>What a sandbox's whole output volume may hold: the files, the result, and the directories around them.</summary>
    internal static SandboxArchiveLimits OutputLimits(SandboxCaps caps) =>
        new(caps.MaxArchiveBytes + caps.MaxResultBytes, caps.MaxArchiveFiles + 2);

    private async Task RemoveAsync(PodmanRunNames names)
    {
        // Uncancellable: a run whose caller gave up still must not leave a container behind.
        foreach (var container in new[] { names.Sandbox, names.Holder })
            await Quietly(() => api.RemoveContainerAsync(container, CancellationToken.None))
                .ConfigureAwait(false);
        foreach (var volume in new[] { names.Input, names.Output })
            await Quietly(() => api.RemoveVolumeAsync(volume, CancellationToken.None))
                .ConfigureAwait(false);
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        foreach (
            var id in await api.ListAsync(volumes: false, LauncherLabel, ct).ConfigureAwait(false)
        )
            await api.RemoveContainerAsync(id, ct).ConfigureAwait(false);
        foreach (
            var name in await api.ListAsync(volumes: true, LauncherLabel, ct).ConfigureAwait(false)
        )
            await api.RemoveVolumeAsync(name, ct).ConfigureAwait(false);
    }

    private async Task Quietly(Func<Task> remove)
    {
        try
        {
            await remove().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is PodmanApiException or HttpRequestException or IOException)
        {
            logger.LogWarning(
                ex,
                "A sandbox resource could not be removed; the next start sweeps it."
            );
        }
    }

    /// <summary>The sandbox: the profile's image under the runtime, every limit applied, nothing from the request but the run id.</summary>
    internal JsonObject SandboxSpec(SandboxJob job, PodmanRunNames names)
    {
        var spec = Hardened(
            names.Sandbox,
            job.Profile.Image,
            job.Profile.User,
            job.Profile.Command,
            job
        );
        spec["oci_runtime"] = ociRuntime;
        spec["hostname"] = "sandbox";
        spec["env"] = new JsonObject
        {
            ["HOME"] = SandboxPaths.Scratch,
            ["TMPDIR"] = SandboxPaths.Scratch,
        };
        spec["oom_score_adj"] = 1000;
        spec["resource_limits"] = new JsonObject
        {
            ["memory"] = new JsonObject
            {
                ["limit"] = job.Limits.MemoryMiB * MiB,
                ["swap"] = job.Limits.MemoryMiB * MiB,
            },
            ["cpu"] = new JsonObject
            {
                ["quota"] = job.Limits.CpuMillicores * 100L,
                ["period"] = 100_000,
            },
            ["pids"] = new JsonObject { ["limit"] = options.Caps.MaxPids },
        };
        spec["volumes"] = new JsonArray(
            Volume(names.Input, $"{SandboxPaths.Root}/{SandboxPaths.Input}", "ro"),
            Volume(names.Output, $"{SandboxPaths.Root}/{SandboxPaths.Output}", "rw")
        );
        spec["mounts"] = new JsonArray(
            new JsonObject
            {
                ["destination"] = SandboxPaths.Scratch,
                ["type"] = "tmpfs",
                ["source"] = "tmpfs",
                ["options"] = new JsonArray(
                    string.Create(CultureInfo.InvariantCulture, $"size={job.Limits.DiskMiB}m"),
                    "nosuid",
                    "nodev"
                ),
            }
        );
        return spec;
    }

    /// <summary>The holder: a pause image, under podman's default runtime, which runs nothing of the sandbox's.</summary>
    internal JsonObject HolderSpec(SandboxJob job, PodmanRunNames names)
    {
        var spec = Hardened(
            names.Holder,
            options.Podman.HolderImage,
            "65534:65534",
            options.Podman.HolderCommand,
            job
        );
        spec["resource_limits"] = new JsonObject
        {
            ["memory"] = new JsonObject { ["limit"] = 64 * MiB },
            ["pids"] = new JsonObject { ["limit"] = 16 },
        };
        spec["volumes"] = new JsonArray(
            Volume(names.Input, "/in", "rw"),
            Volume(names.Output, "/out", "ro")
        );
        return spec;
    }

    private static JsonObject Hardened(
        string name,
        string image,
        string user,
        IList<string> command,
        SandboxJob job
    )
    {
        var spec = new JsonObject
        {
            ["name"] = name,
            ["image"] = image,
            ["user"] = user,
            ["labels"] = new JsonObject { [LauncherLabel] = "1", [RunLabel] = job.RunId },
            ["netns"] = new JsonObject { ["nsmode"] = "none" },
            ["ipcns"] = new JsonObject { ["nsmode"] = "private" },
            ["read_only_filesystem"] = true,
            ["no_new_privileges"] = true,
            ["privileged"] = false,
            ["cap_drop"] = new JsonArray("all"),
            ["image_volume_mode"] = "tmpfs",
            ["stop_timeout"] = 0,
            // No log: conmon would write whatever the sandbox prints to the host's disk, unbounded.
            ["log_configuration"] = new JsonObject { ["driver"] = "none" },
        };
        if (command.Count > 0)
            spec["command"] = new JsonArray(command.Select(c => (JsonNode?)c).ToArray());
        return spec;
    }

    private static JsonObject Volume(string name, string destination, string mode) =>
        new()
        {
            ["Name"] = name,
            ["Dest"] = destination,
            ["Options"] = new JsonArray(mode, "nosuid", "nodev", "noexec"),
        };

    private static Dictionary<string, string> Tmpfs(long bytes, string mode) =>
        new(StringComparer.Ordinal)
        {
            ["type"] = "tmpfs",
            ["device"] = "tmpfs",
            ["o"] = string.Create(
                CultureInfo.InvariantCulture,
                $"size={bytes},mode={mode},nosuid,nodev,noexec"
            ),
        };

    private static Dictionary<string, string> Labels(SandboxJob job) =>
        new(StringComparer.Ordinal) { [LauncherLabel] = "1", [RunLabel] = job.RunId };
}
