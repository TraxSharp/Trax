using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;
using Trax.Samples.Bakery.E2E.Fixtures;

namespace Trax.Samples.Bakery.Browser;

/// <summary>
/// Builds the page once against a host started as a process of its own on a free port, and launches chromium. A
/// harness that cannot start fails the run rather than every test reporting its own red (Samples ADR 0001). The page is
/// served from the host's own origin by the browser context, from the built files, and every <c>/trax</c> and
/// <c>/dev</c> request goes on to the host: one origin, as a deployed page beside its API has.
/// </summary>
[SetUpFixture]
public class BrowserSetup
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=bakery_browser_tests;Username=trax;Password=trax123;"
        + "Maximum Pool Size=20;Minimum Pool Size=0";

    private static Process? s_host;

    // What the host wrote, shown when it does not come up.
    private static readonly StringBuilder HostLog = new();
    private static IPlaywright? s_playwright;

    public static string Dist { get; private set; } = null!;

    public static IBrowser Browser { get; private set; } = null!;

    public static Uri Api { get; private set; } = null!;

    /// <summary>What the pages' consoles said and which of their requests failed, for a failing test's message.</summary>
    public static System.Collections.Concurrent.ConcurrentQueue<string> Seen { get; } = new();

    private static string SamplesFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Trax.Samples.slnx")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Trax.Samples.slnx not found above the tests.");
    }

    public static string WebFolder { get; } =
        Path.Combine(SamplesFolder(), "samples", "Bakery", "Trax.Samples.Bakery.Web");

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        Api = new Uri($"http://127.0.0.1:{FreePort()}");
        s_host = StartHost();
        await WaitForHealth();
        Dist = await Build();
        s_playwright = await Playwright.CreateAsync();
        Browser = await s_playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (Browser is not null)
            await Browser.CloseAsync();
        s_playwright?.Dispose();
        if (s_host is { HasExited: false })
        {
            s_host.Kill(entireProcessTree: true);
            await s_host.WaitForExitAsync();
        }
        s_host?.Dispose();
    }

    /// <summary>A browser context on the built page: its files from <see cref="Dist"/>, its API calls to the host.</summary>
    public static async Task<IPage> NewPageAsync()
    {
        var context = await Browser.NewContextAsync();
        await context.RouteAsync(
            $"{Api.ToString().TrimEnd('/')}/**",
            async route =>
            {
                var path = new Uri(route.Request.Url).AbsolutePath.TrimStart('/');
                if (
                    path.StartsWith("trax/", StringComparison.Ordinal)
                    || path.StartsWith("dev/", StringComparison.Ordinal)
                )
                {
                    await route.ContinueAsync();
                    return;
                }
                var file = Path.Combine(Dist, path.Length == 0 ? "index.html" : path);
                if (!File.Exists(file))
                    file = Path.Combine(Dist, "index.html");
                await route.FulfillAsync(
                    new()
                    {
                        BodyBytes = await File.ReadAllBytesAsync(file),
                        ContentType =
                            file.EndsWith(".js") ? "text/javascript"
                            : file.EndsWith(".css") ? "text/css"
                            : "text/html",
                    }
                );
            }
        );
        var page = await context.NewPageAsync();
        page.Console += (_, message) => Seen.Enqueue($"console: {message.Text}");
        page.RequestFailed += (_, request) =>
            Seen.Enqueue($"failed: {request.Url} {request.Failure}");
        await page.GotoAsync(Api.ToString());
        return page;
    }

    private static Process StartHost()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (
            var arg in new[]
            {
                "Trax.Samples.Bakery.Api.dll",
                "--urls",
                Api.ToString().TrimEnd('/'),
            }
        )
            start.ArgumentList.Add(arg);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        // appsettings.Development.json names the demo's endpoint, which outranks --urls.
        start.Environment["Kestrel__Endpoints__Http__Url"] = Api.ToString().TrimEnd('/');
        start.Environment["ConnectionStrings__TraxDatabase"] = TestPostgres.WithPort(
            DefaultConnectionString
        );
        start.Environment["Bakery__PageOrigin"] = Api.ToString().TrimEnd('/');
        start.Environment["Bakery__MachineSweepInterval"] = "00:00:00.200";
        var process = Process.Start(start)!;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (HostLog)
                    HostLog.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                lock (HostLog)
                    HostLog.AppendLine(e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitForHealth()
    {
        using var http = new HttpClient { BaseAddress = Api, Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(1);
        var last = "no answer";
        while (DateTime.UtcNow < deadline)
        {
            if (s_host!.HasExited)
                throw new InvalidOperationException(
                    $"The Bakery host exited with {s_host.ExitCode}:\n{HostLog}"
                );
            try
            {
                var status = (await http.GetAsync("/trax/health")).StatusCode;
                if (status == HttpStatusCode.OK)
                    return;
                last = status.ToString();
            }
            catch (HttpRequestException ex)
            {
                last = ex.Message;
            }
            catch (TaskCanceledException)
            {
                last = "timed out";
            }
            // determinism: polls the starting host until it answers, bounded by the deadline above.
            await Task.Delay(250);
        }
        throw new TimeoutException(
            $"The Bakery host did not answer /trax/health within a minute (last: {last}):\n{HostLog}"
        );
    }

    // The page built with vite into this project's output, pointed at the host. Its npm packages, and the engine's in
    // Trax.StateMachine/ts, must be installed; CI installs them before the tests.
    private static async Task<string> Build()
    {
        var vite = Path.Combine(WebFolder, "node_modules", "vite", "bin", "vite.js");
        if (!File.Exists(vite))
            throw new InvalidOperationException(
                $"The Bakery page's npm packages are not installed. Run `npm ci --ignore-scripts` in {WebFolder} "
                    + "and in Trax.StateMachine/ts before the browser tests."
            );
        var output = Path.Combine(AppContext.BaseDirectory, "bakery-web");
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = WebFolder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { vite, "build", "--outDir", output, "--emptyOutDir" })
            start.ArgumentList.Add(arg);
        start.Environment["VITE_BAKERY_API"] = Api.ToString().TrimEnd('/');
        using var process = Process.Start(start)!;
        var log = new StringBuilder();
        var reading = Task.WhenAll(
            Drain(process.StandardOutput, log),
            Drain(process.StandardError, log)
        );
        await process.WaitForExitAsync();
        await reading;
        if (process.ExitCode != 0 || !File.Exists(Path.Combine(output, "index.html")))
            throw new InvalidOperationException($"vite build failed ({process.ExitCode}):\n{log}");
        return output;
    }

    private static async Task Drain(StreamReader reader, StringBuilder log)
    {
        while (await reader.ReadLineAsync() is { } line)
            lock (log)
                log.AppendLine(line);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
