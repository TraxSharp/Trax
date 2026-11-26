using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Trax.Core.Exceptions;
using Trax.Effect.Services.EffectJunction;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Image;

/// <summary>
/// The acts. <c>echo</c> behaves: it returns its first target and writes it to a file. Every other
/// act tries something a sandbox must stop, and reports how far it got.
/// </summary>
public sealed class HostileJunction(SandboxWorkspace workspace)
    : EffectJunction<HostileInput, HostileOutput>
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    public override Task<HostileOutput> Run(HostileInput input) =>
        input.Act switch
        {
            "echo" => EchoAsync(input),
            "probe" => ProbeAsync(input),
            "write-outside" => WriteOutsideAsync(input),
            "fork-bomb" => ForkBombAsync(),
            "memory" => Task.FromResult(Hog()),
            "sleep" => SleepAsync(),
            "fill-disk" => FillDiskAsync(),
            "fill-disk-unlinked" => FillDiskThroughAnUnlinkedFileAsync(),
            "symlink" => Task.FromResult(Link(input)),
            "many-files" => ManyFilesAsync(input),
            "fail" => throw new TrainException(
                input.Targets.FirstOrDefault() ?? "failed on purpose"
            ),
            _ => throw new TrainException($"No act '{input.Act}'."),
        };

    private async Task<HostileOutput> EchoAsync(HostileInput input)
    {
        var text = input.Targets.FirstOrDefault() ?? "";
        Directory.CreateDirectory(Path.Combine(workspace.FilesDirectory, "nested"));
        await File.WriteAllTextAsync(
                Path.Combine(workspace.FilesDirectory, "echo.txt"),
                text,
                CancellationToken
            )
            .ConfigureAwait(false);
        await File.WriteAllTextAsync(
                Path.Combine(workspace.FilesDirectory, "nested", "again.txt"),
                text,
                CancellationToken
            )
            .ConfigureAwait(false);
        return new HostileOutput([], [], text);
    }

    /// <summary>Tries a TCP connection to every <c>host:port</c> target, and a DNS lookup of every bare name.</summary>
    private async Task<HostileOutput> ProbeAsync(HostileInput input)
    {
        var reached = new List<string>();
        foreach (var target in input.Targets)
        {
            var colon = target.LastIndexOf(':');
            if (colon < 0)
            {
                if (await ResolvesAsync(target).ConfigureAwait(false))
                    reached.Add(target);
                continue;
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
            deadline.CancelAfter(ProbeTimeout);
            using var client = new TcpClient();
            try
            {
                await client
                    .ConnectAsync(
                        target[..colon].Trim('[', ']'),
                        int.Parse(target[(colon + 1)..], CultureInfo.InvariantCulture),
                        deadline.Token
                    )
                    .ConfigureAwait(false);
                reached.Add(target);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                // Blocked, as it should be.
            }
        }
        return new HostileOutput(reached, [], "");
    }

    private async Task<bool> ResolvesAsync(string name)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        deadline.CancelAfter(ProbeTimeout);
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(name, deadline.Token)
                .ConfigureAwait(false);
            return addresses.Length > 0;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<HostileOutput> WriteOutsideAsync(HostileInput input)
    {
        var written = new List<string>();
        foreach (var path in input.Targets)
        {
            try
            {
                await File.WriteAllTextAsync(path, "escaped", CancellationToken)
                    .ConfigureAwait(false);
                written.Add(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Refused, as it should be.
            }
        }
        return new HostileOutput([], written, "");
    }

    /// <summary>Starts a shell fork bomb and waits; the pids limit holds it and the timeout ends it.</summary>
    private async Task<HostileOutput> ForkBombAsync()
    {
        using var bomb = Process.Start(
            new ProcessStartInfo("/bin/sh", ["-c", "b() { b | b & }; b"])
            {
                UseShellExecute = false,
            }
        );
        // determinism: runs until the launcher kills it; that kill is what the test waits on.
        await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, CancellationToken)
            .ConfigureAwait(false);
        return new HostileOutput([], [], "");
    }

    /// <summary>
    /// Allocates and touches native memory until the kernel kills the sandbox. Native, so the
    /// .NET GC's own heap limit (a share of the cgroup's) does not stop it first.
    /// </summary>
    private static HostileOutput Hog()
    {
        var chunk = new byte[16 * 1024 * 1024];
        Array.Fill(chunk, (byte)1);
        var hoard = new List<IntPtr>();
        while (true)
        {
            var block = System.Runtime.InteropServices.Marshal.AllocHGlobal(chunk.Length);
            System.Runtime.InteropServices.Marshal.Copy(chunk, 0, block, chunk.Length);
            hoard.Add(block);
        }
    }

    private async Task<HostileOutput> SleepAsync()
    {
        // determinism: runs until the launcher kills it; that kill is what the test waits on.
        await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, CancellationToken)
            .ConfigureAwait(false);
        return new HostileOutput([], [], "");
    }

    /// <summary>Writes to its output until it is full, then leaves the full file there.</summary>
    private async Task<HostileOutput> FillDiskAsync()
    {
        var chunk = new byte[1024 * 1024];
        var stream = File.Create(Path.Combine(workspace.FilesDirectory, "fill.bin"));
        await using (stream.ConfigureAwait(false))
            while (true)
                await stream.WriteAsync(chunk, CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a file in its output while holding it open, so no walk of the directory sees it, and
    /// writes to it until the volume is full. Closes it before answering, which frees the space,
    /// and reports how many bytes the volume took.
    /// </summary>
    private async Task<HostileOutput> FillDiskThroughAnUnlinkedFileAsync()
    {
        var chunk = new byte[1024 * 1024];
        var path = Path.Combine(workspace.FilesDirectory, "hidden.bin");
        long written = 0;
        var stream = File.Create(path);
        await using (stream.ConfigureAwait(false))
        {
            File.Delete(path);
            try
            {
                while (true)
                {
                    await stream.WriteAsync(chunk, CancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(CancellationToken).ConfigureAwait(false);
                    written += chunk.Length;
                }
            }
            catch (IOException) { }
        }
        return new HostileOutput([], [], written.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Leaves a symbolic link in its output, pointing at the first target.</summary>
    private HostileOutput Link(HostileInput input)
    {
        File.CreateSymbolicLink(
            Path.Combine(workspace.FilesDirectory, "link"),
            input.Targets.FirstOrDefault() ?? "/etc/passwd"
        );
        return new HostileOutput([], [], "");
    }

    private async Task<HostileOutput> ManyFilesAsync(HostileInput input)
    {
        for (var i = 0; i < input.Count; i++)
            await File.WriteAllTextAsync(
                    Path.Combine(
                        workspace.FilesDirectory,
                        i.ToString(CultureInfo.InvariantCulture)
                    ),
                    "x",
                    CancellationToken
                )
                .ConfigureAwait(false);
        return new HostileOutput([], [], "");
    }
}
