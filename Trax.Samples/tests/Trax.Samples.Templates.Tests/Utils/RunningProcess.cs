using System.Diagnostics;
using System.Text;

namespace Trax.Samples.Templates.Tests.Utils;

/// <summary>An application started by <see cref="Dotnet.Start"/>; disposing it kills it.</summary>
internal sealed class RunningProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string[] _args;
    private readonly StringBuilder _output = new();
    private readonly Lock _gate = new();

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
            await Task.Delay(TimeSpan.FromMilliseconds(200)).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
            _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync().ConfigureAwait(false);
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
