using System.Diagnostics;
using System.Globalization;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy;

/// <summary>
/// Runs the project with <c>--trax-export-deploy</c>: <c>dotnet run</c> in Release, without its launch profile, so the
/// host starts as it would deployed (Production) and refuses what it would refuse there. A host that ignores the
/// argument starts serving instead of exiting, so the run is stopped after <see cref="Timeout"/>, or when the command
/// is cancelled.
/// </summary>
internal sealed class DotnetHostExporter(CancellationToken ct = default) : IHostExporter
{
    /// <summary>How long one export may take, its build included.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public async Task<DeployManifest> ExportAsync(
        string projectPath,
        IReadOnlyDictionary<string, string> environment
    )
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-deploy-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "deploy-manifest.json");
        try
        {
            var psi = StartInfo(projectPath, path, environment);
            (int ExitCode, string StdOut, string StdErr) run;
            try
            {
                run = await ChildProcess.RunAsync(psi, Timeout, ct).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(projectPath)} did not report what it needs from its deployment: "
                        + "it was still running when trax stopped it. A host that calls app.Run() instead of "
                        + $"'await app.RunTraxAsync(args)' serves instead of reporting. {ex.Message}",
                    ex
                );
            }
            var (exit, stdout, stderr) = run;
            if (exit != 0 || !File.Exists(path))
                throw new InvalidOperationException(
                    $"{Path.GetFileName(projectPath)} did not report what it needs from its deployment "
                        + string.Create(
                            CultureInfo.InvariantCulture,
                            $"(exit {exit}). A host reports it from 'await app.RunTraxAsync(args)'; one that calls "
                        )
                        + $"app.Run() refuses. Its output:\n{stdout}{stderr}"
                );
            return DeployManifest.Read(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    internal static ProcessStartInfo StartInfo(
        string projectPath,
        string manifestPath,
        IReadOnlyDictionary<string, string> environment
    )
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(projectPath).NotNull(),
            UseShellExecute = false,
        };
        foreach (
            var arg in new[]
            {
                "run",
                "--project",
                projectPath,
                "-c",
                "Release",
                "--no-launch-profile",
                "--",
                TraxDeployExport.Argument,
                manifestPath,
            }
        )
            psi.ArgumentList.Add(arg);
        psi.Environment.Remove("DOTNET_ENVIRONMENT");
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        foreach (var (name, value) in environment)
            psi.Environment[name] = value;
        return psi;
    }

    /// <summary>
    /// What the export runs with: a value for every connection string, setting and secret the deployment will
    /// supply, so a host that reads them at startup starts. None of them reaches anything: the export opens no
    /// database and connects to nothing.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Placeholders(DeployFile file)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"ConnectionStrings__{file.ConnectionStrings.Trax}"] =
                "Host=trax-deploy-export.invalid;Database=trax;Username=trax;Password=trax",
            [$"ConnectionStrings__{file.ConnectionStrings.RabbitMq}"] =
                "amqp://trax:trax@trax-deploy-export.invalid:5672",
        };
        // Settings are often addresses (an issuer, an origin), which a host may check are URLs when it starts.
        foreach (var name in file.Settings)
            values[name] = "https://trax-deploy-export.invalid";
        foreach (var name in file.Secrets)
            values[name] = "trax-deploy-export";
        return values;
    }
}
