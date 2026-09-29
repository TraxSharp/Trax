using System.Diagnostics;
using FluentAssertions;
using Trax.Cli.Commands;

namespace Trax.Cli.Tests.IntegrationTests;

/// <summary>
/// The tool is installed as <c>trax</c>, so its help must say <c>trax</c>. System.CommandLine names the root
/// command after the running assembly, not the tool command, which is why the assembly itself is called
/// <c>trax</c>. These run the built entry point in a child process, because the root name comes from the
/// process's own command line.
/// </summary>
public class HelpUsageTests
{
    private static string EntryAssembly => typeof(GenerateCommand).Assembly.Location;

    private static (int ExitCode, string StdOut) RunCli(params string[] args)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add(EntryAssembly);
        foreach (var arg in args)
            info.ArgumentList.Add(arg);

        using var process = Process.Start(info)!;
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        process
            .WaitForExit(TimeSpan.FromSeconds(60))
            .Should()
            .BeTrue("`trax --help` returns at once");
        return (process.ExitCode, stdOutTask.Result + stdErrTask.Result);
    }

    [Test]
    public void The_entry_assembly_is_named_after_the_tool_command()
    {
        Path.GetFileNameWithoutExtension(EntryAssembly).Should().Be("trax");
    }

    [Test]
    public void Root_help_shows_the_trax_command_name()
    {
        var (exitCode, output) = RunCli("--help");

        exitCode.Should().Be(0);
        output.Should().Contain("Usage:\n  trax [command] [options]".ReplaceLineEndings());
        output.Should().NotContain("Trax.Cli [command]");
    }

    [Test]
    public void Subcommand_help_shows_the_trax_command_name()
    {
        var (exitCode, output) = RunCli("machine", "--help");

        exitCode.Should().Be(0);
        output.Should().Contain("trax machine [command] [options]");
    }
}
