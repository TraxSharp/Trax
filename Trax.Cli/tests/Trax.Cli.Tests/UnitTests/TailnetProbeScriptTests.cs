using System.Diagnostics;
using AwesomeAssertions;
using Trax.Cli.Deploy.Kubernetes;
using Trax.Core.Extensions;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// Runs the tailnet probe's script under <c>/bin/sh</c> with <c>nslookup</c>, <c>wget</c> and <c>sleep</c> replaced by
/// stubs that resolve and reach only the names a case lists, so nothing touches a network. The script passes only when
/// the control listener answers and the canary does not: a probe that cannot connect at all proves nothing.
/// </summary>
public class TailnetProbeScriptTests
{
    private const string Canary = KubernetesTailnet.CanaryName;
    private const string Control = KubernetesTailnet.ControlName;

    [Test]
    public async Task The_control_answering_and_the_canary_refusing_passes()
    {
        var (code, output) = await RunAsync(reachable: [Control]).ConfigureAwait(false);

        code.Should().Be(0, output);
        output.Should().Contain("refused, as the policy says");
    }

    [Test]
    public async Task The_canary_answering_fails()
    {
        var (code, output) = await RunAsync(reachable: [Control, Canary]).ConfigureAwait(false);

        code.Should().Be(1, output);
    }

    [Test]
    public async Task Nothing_answering_proves_nothing_and_fails()
    {
        var (code, output) = await RunAsync(reachable: []).ConfigureAwait(false);

        code.Should().Be(3, output);
        output.Should().Contain($"never reached {Control}");
    }

    [Test]
    public async Task A_name_that_does_not_resolve_fails()
    {
        var (code, output) = await RunAsync(reachable: [Control], unresolved: [Control])
            .ConfigureAwait(false);

        code.Should().Be(2, output);
        output.Should().Contain($"{Control} does not resolve");
    }

    private static async Task<(int Code, string Output)> RunAsync(
        string[] reachable,
        string[]? unresolved = null
    )
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/sh"))
            Assert.Ignore("needs a POSIX shell");
        var stubs = Directory.CreateTempSubdirectory("trax-probe-");
        try
        {
            await StubAsync(
                    stubs,
                    "nslookup",
                    "case \" $UNRESOLVED \" in *\" $1 \"*) exit 1;; esac\nexit 0"
                )
                .ConfigureAwait(false);
            await StubAsync(
                    stubs,
                    "wget",
                    "for a; do url=$a; done\nhost=${url#http://}\nhost=${host%%:*}\n"
                        + "case \" $REACHABLE \" in *\" $host \"*) exit 0;; esac\nexit 1"
                )
                .ConfigureAwait(false);
            await StubAsync(stubs, "sleep", "exit 0").ConfigureAwait(false);
            var info = new ProcessStartInfo("/bin/sh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(KubernetesTailnet.ProbeScript);
            info.Environment["PATH"] = $"{stubs.FullName}:/usr/bin:/bin";
            info.Environment["REACHABLE"] = string.Join(' ', reachable);
            info.Environment["UNRESOLVED"] = string.Join(' ', unresolved ?? []);
            using var process = Process.Start(info).NotNull();
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            // Every sleep is stubbed, so even thirty tries take well under a second.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return (
                process.ExitCode,
                await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false)
            );
        }
        finally
        {
            stubs.Delete(recursive: true);
        }
    }

    private static async Task StubAsync(DirectoryInfo dir, string name, string body)
    {
        var path = Path.Combine(dir.FullName, name);
        await File.WriteAllTextAsync(path, $"#!/bin/sh\n{body}\n").ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
    }
}
