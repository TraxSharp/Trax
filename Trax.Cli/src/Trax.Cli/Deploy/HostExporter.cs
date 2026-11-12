using System.Diagnostics;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy;

/// <summary>Asks a project's host what it needs from its deployment.</summary>
internal interface IHostExporter
{
    DeployManifest Export(string projectPath, IReadOnlyDictionary<string, string> environment);
}

/// <summary>
/// Runs the project with <c>--trax-export-deploy</c>: <c>dotnet run</c> in Release, without its launch profile, so the
/// host starts as it would deployed (Production) and refuses what it would refuse there.
/// </summary>
internal sealed class DotnetHostExporter : IHostExporter
{
    public DeployManifest Export(
        string projectPath,
        IReadOnlyDictionary<string, string> environment
    )
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-deploy-{Guid.NewGuid():N}");
        var path = Path.Combine(dir, "deploy-manifest.json");
        try
        {
            var psi = StartInfo(projectPath, path, environment);
            var (exit, stdout, stderr) = ChildProcess.Run(psi);
            if (exit != 0 || !File.Exists(path))
                throw new InvalidOperationException(
                    $"{Path.GetFileName(projectPath)} did not report what it needs from its deployment "
                        + $"(exit {exit}). A host reports it from 'await app.RunTraxAsync(args)'; one that calls "
                        + $"app.Run() refuses. Its output:\n{stdout}{stderr}"
                );
            return DeployManifest.Read(File.ReadAllText(path));
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
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
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
