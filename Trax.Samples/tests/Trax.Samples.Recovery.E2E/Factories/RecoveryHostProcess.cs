using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Recovery.E2E.Factories;

/// <summary>
/// The Recovery host as a process of its own, on its own database (<c>recovery_restart_e2e_tests</c>), so
/// a test can kill it the way a crash does. Disposing an in-process host stops its runs and records them
/// <c>Cancelled</c>; a killed process records nothing, and its runs stay <c>InProgress</c> until a host
/// that starts afterwards reaps them.
/// </summary>
public sealed class RecoveryHostProcess : IAsyncDisposable
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=recovery_restart_e2e_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=10;Minimum Pool Size=0";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(90);

    private readonly Process _process;
    private readonly StringBuilder _output;

    private RecoveryHostProcess(Process process, StringBuilder output, Uri address)
    {
        _process = process;
        _output = output;
        Http = new HttpClient { BaseAddress = address };
    }

    public static string ConnectionString => TestPostgres.WithPort(DefaultConnectionString);

    /// <summary>A client for the host's HTTP endpoint.</summary>
    public HttpClient Http { get; }

    /// <summary>What the process wrote, for a failure message.</summary>
    public string Output
    {
        get
        {
            lock (_output)
                return _output.ToString();
        }
    }

    /// <summary>
    /// Starts the host's build output with <c>dotnet</c>, on a free loopback port, and waits until its
    /// health endpoint answers.
    /// </summary>
    /// <param name="stepDelay">How long each junction takes, so a test can catch a run in the middle.</param>
    public static async Task<RecoveryHostProcess> StartAsync(TimeSpan stepDelay)
    {
        var port = FreePort();
        var address = new Uri($"http://127.0.0.1:{port}");
        var assembly = HostAssembly();

        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(assembly)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(assembly);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ConnectionStrings__TraxDatabase"] = ConnectionString;
        start.Environment["Kestrel__Endpoints__Http__Url"] = address.ToString();
        start.Environment["Recovery__StepDelay"] = stepDelay.ToString("c");
        start.Environment["Recovery__ModelLatencyMin"] = "00:00:00";
        start.Environment["Recovery__ModelLatencyMax"] = "00:00:00";

        var output = new StringBuilder();
        var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => Append(output, e.Data);
        process.ErrorDataReceived += (_, e) => Append(output, e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var host = new RecoveryHostProcess(process, output, address);
        var healthy = await Polling.WaitUntilAsync(
            async () =>
            {
                if (process.HasExited)
                    return true;
                try
                {
                    using var response = await host.Http.GetAsync("/trax/health");
                    return response.StatusCode == HttpStatusCode.OK;
                }
                catch (HttpRequestException)
                {
                    return false;
                }
            },
            StartTimeout,
            TimeSpan.FromMilliseconds(250)
        );
        if (!healthy || process.HasExited)
        {
            await host.DisposeAsync();
            throw new InvalidOperationException(
                $"The Recovery host did not start on {address}. It wrote:\n{host.Output}"
            );
        }
        return host;
    }

    /// <summary>Kills the process at once, as a crash would: no shutdown, nothing recorded.</summary>
    public async Task KillAsync()
    {
        if (!_process.HasExited)
            _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await KillAsync();
        _process.Dispose();
        Http.Dispose();
    }

    private static void Append(StringBuilder output, string? line)
    {
        if (line is null)
            return;
        lock (output)
            output.AppendLine(line);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    // The host project's own build output, which carries its runtime config and dependencies. The test
    // project references the host, so it is built in the same configuration.
    private static string HostAssembly()
    {
        var testBin = new DirectoryInfo(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)
        );
        var framework = testBin.Name;
        var configuration = testBin.Parent!.Name;
        var samples = testBin;
        while (
            samples is not null && !File.Exists(Path.Combine(samples.FullName, "Trax.Samples.slnx"))
        )
            samples = samples.Parent;
        if (samples is null)
            throw new InvalidOperationException(
                $"Could not find Trax.Samples.slnx above {AppContext.BaseDirectory}."
            );

        var assembly = Path.Combine(
            samples.FullName,
            "samples",
            "Recovery",
            "Trax.Samples.Recovery.Api",
            "bin",
            configuration,
            framework,
            "Trax.Samples.Recovery.Api.dll"
        );
        return File.Exists(assembly)
            ? assembly
            : throw new InvalidOperationException($"The host is not built at {assembly}.");
    }
}
