using System.Diagnostics;
using System.Text;

namespace Trax.Samples.Recovery.Browser.Fixtures;

/// <summary>
/// The Recovery page, built once per run with <c>vite build</c> into this project's output, as a deploy would
/// build it. Its npm packages must be installed (<c>npm ci</c> in the client and in Trax.Api.StateMachine, whose
/// engine source the page compiles); CI installs them before the tests.
/// </summary>
public static class RecoveryClientBuild
{
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(3);

    /// <summary>The page's own folder, <c>samples/Recovery/Trax.Samples.Recovery.Client</c>.</summary>
    public static string ClientFolder { get; } =
        Path.Combine(SamplesFolder(), "samples", "Recovery", "Trax.Samples.Recovery.Client");

    /// <summary>Builds the page and returns the folder holding <c>index.html</c> and its assets.</summary>
    public static async Task<string> BuildAsync()
    {
        var vite = Path.Combine(ClientFolder, "node_modules", "vite", "bin", "vite.js");
        var engineModules = Path.Combine(
            ClientFolder,
            "..",
            "..",
            "..",
            "..",
            "Trax.Api.StateMachine",
            "node_modules"
        );
        if (!File.Exists(vite) || !Directory.Exists(engineModules))
            throw new InvalidOperationException(
                "The Recovery page's npm packages are not installed. Run `npm ci --ignore-scripts` in "
                    + $"{ClientFolder} and in Trax.Api.StateMachine before the browser tests."
            );

        var output = Path.Combine(AppContext.BaseDirectory, "recovery-client");
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = ClientFolder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { vite, "build", "--outDir", output, "--emptyOutDir" })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        var log = new StringBuilder();
        var reading = Task.WhenAll(
            Drain(process.StandardOutput, log),
            Drain(process.StandardError, log)
        );
        using var timeout = new CancellationTokenSource(BuildTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"vite build did not finish in {BuildTimeout}:\n{log}");
        }
        await reading;

        if (process.ExitCode != 0 || !File.Exists(Path.Combine(output, "index.html")))
            throw new InvalidOperationException(
                $"vite build failed with exit code {process.ExitCode}:\n{log}"
            );
        return output;
    }

    private static async Task Drain(StreamReader reader, StringBuilder log)
    {
        while (await reader.ReadLineAsync() is { } line)
            lock (log)
                log.AppendLine(line);
    }

    private static string SamplesFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            folder is not null && !File.Exists(Path.Combine(folder.FullName, "Trax.Samples.slnx"))
        )
            folder = folder.Parent;
        return folder?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find Trax.Samples.slnx above {AppContext.BaseDirectory}."
            );
    }
}
