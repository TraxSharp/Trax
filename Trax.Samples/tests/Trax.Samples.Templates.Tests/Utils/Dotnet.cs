using System.Diagnostics;
using System.Text;

namespace Trax.Samples.Templates.Tests.Utils;

/// <summary>
/// Runs the dotnet CLI as a consumer would, outside the test host's MSBuild environment.
/// </summary>
internal static class Dotnet
{
    // Every process is waited on with this ceiling, so a hung restore or build fails the test
    // with its output instead of running into the runner's own timeout.
    public static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(5);

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Trax.Samples.slnx")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Trax.Samples.slnx not found above the tests");
    }

    public static Task<(int ExitCode, string Output)> Run(
        string workingDirectory,
        params string[] args
    ) => Run(workingDirectory, allowFailure: false, args);

    public static async Task<(int ExitCode, string Output)> Run(
        string workingDirectory,
        bool allowFailure,
        params string[] args
    )
    {
        using var process = Process.Start(StartInfo(workingDirectory, args))!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"dotnet {string.Join(' ', args)} did not finish within {ProcessTimeout}"
            );
        }

        var output = await stdout + await stderr;
        if (!allowFailure && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet {string.Join(' ', args)} exited {process.ExitCode}:\n{output}"
            );
        return (process.ExitCode, output);
    }

    /// <summary>
    /// Starts a long-running dotnet process (an application) and returns once its output
    /// contains <paramref name="ready"/>. Throws with the output so far if the process exits
    /// first or does not get there within <see cref="ProcessTimeout"/>.
    /// </summary>
    public static async Task<RunningProcess> Start(
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        string ready,
        params string[] args
    )
    {
        var psi = StartInfo(workingDirectory, args);
        foreach (var (name, value) in environment)
            psi.Environment[name] = value;

        var running = new RunningProcess(Process.Start(psi)!, args);
        await running.WaitFor(ready);
        return running;
    }

    private static ProcessStartInfo StartInfo(string workingDirectory, string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        // Keep the child out of the test host's MSBuild environment.
        psi.Environment.Remove("MSBuildSDKsPath");
        psi.Environment.Remove("MSBuildExtensionsPath");
        psi.Environment.Remove("MSBUILD_EXE_PATH");
        // The child chooses its own environment; the runner's must not leak into it.
        psi.Environment.Remove("ASPNETCORE_ENVIRONMENT");
        psi.Environment.Remove("DOTNET_ENVIRONMENT");
        psi.Environment.Remove("ASPNETCORE_URLS");
        return psi;
    }
}

/// <summary>An application started by <see cref="Dotnet.Start"/>; disposing it kills it.</summary>
internal sealed class RunningProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string[] _args;
    private readonly StringBuilder _output = new();
    private readonly object _gate = new();

    public RunningProcess(Process process, string[] args)
    {
        _process = process;
        _args = args;
        _process.OutputDataReceived += (_, e) => Append(e.Data);
        _process.ErrorDataReceived += (_, e) => Append(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public string Output
    {
        get
        {
            lock (_gate)
                return _output.ToString();
        }
    }

    public async Task WaitFor(string text)
    {
        var deadline = DateTime.UtcNow + Dotnet.ProcessTimeout;
        while (!Output.Contains(text, StringComparison.Ordinal))
        {
            if (_process.HasExited)
                throw new InvalidOperationException(
                    $"dotnet {string.Join(' ', _args)} exited {_process.ExitCode} before printing "
                        + $"'{text}':\n{Output}"
                );
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(
                    $"dotnet {string.Join(' ', _args)} did not print '{text}' within "
                        + $"{Dotnet.ProcessTimeout}:\n{Output}"
                );
            // determinism: polls for the condition, bounded by ProcessTimeout above.
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
            _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync();
        _process.Dispose();
    }

    private void Append(string? line)
    {
        if (line is null)
            return;
        lock (_gate)
            _output.AppendLine(line);
    }
}
