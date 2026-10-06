using System.Diagnostics;
using AwesomeAssertions;
using Trax.Cli.Generator;

namespace Trax.Cli.Tests.IntegrationTests;

/// <summary>
/// The published <c>Trax.Samples.Templates</c> installed into a private template hive
/// (<c>--debug:custom-hive</c>), so a test can run the real <c>dotnet new trax-hub</c> everywhere, CI included,
/// without the template being installed on the machine and without touching the machine's templates.
/// </summary>
internal sealed class TemplateHive : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private readonly string _root;
    private readonly string _hive;

    private TemplateHive(string root, string hive)
    {
        _root = root;
        _hive = hive;
    }

    public static TemplateHive Install()
    {
        var root = Path.Combine(Path.GetTempPath(), $"trax-cli-hive-{Guid.NewGuid():N}");
        var hive = Path.Combine(root, "hive");
        Directory.CreateDirectory(hive);

        var install = Dotnet(
            root,
            "new",
            "install",
            "Trax.Samples.Templates",
            "--debug:custom-hive",
            hive
        );
        install.ExitCode.Should().Be(0, $"the template package installs:\n{install.Output}");
        return new TemplateHive(root, hive);
    }

    /// <summary>A generator whose hub step is <c>dotnet new trax-hub</c> from this hive.</summary>
    public TraxProjectGenerator Generator() => new(ScaffoldHub);

    private void ScaffoldHub(string name, string dir)
    {
        var scaffold = Dotnet(
            _root,
            "new",
            "trax-hub",
            "-n",
            name,
            "-o",
            dir,
            "--debug:custom-hive",
            _hive
        );
        if (scaffold.ExitCode != 0)
            throw new InvalidOperationException(
                TraxProjectGenerator.DotnetNewFailure(scaffold.ExitCode, scaffold.Output)
            );
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static (int ExitCode, string Output) Dotnet(
        string workingDirectory,
        params string[] args
    )
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

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"dotnet {string.Join(' ', args)} did not finish within {Timeout}.");
        }
        return (process.ExitCode, stdout.Result + stderr.Result);
    }
}
