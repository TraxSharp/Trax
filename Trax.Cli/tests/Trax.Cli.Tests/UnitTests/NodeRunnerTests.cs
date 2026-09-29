using FluentAssertions;
using Trax.Cli.Machines;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <see cref="NodeRunner"/> spawns a real process and captures its exit code and streams. These exercise that
/// path without depending on node being installed by using <c>dotnet</c> (always present in a .NET test run)
/// as the stand-in executable, plus the not-installed detection with a bogus binary.
///
/// <para>Enforces <c>docs/adr/0001-the-machine-toolchain-is-half-in-process.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0001-the-machine-toolchain-is-half-in-process.md")]
public class NodeRunnerTests
{
    [Test]
    public void Run_captures_the_exit_code_and_stdout()
    {
        var result = new NodeRunner("dotnet").Run("--version", []);

        result
            .ExitCode.Should()
            .Be(
                0,
                "the TypeScript half of the toolchain is a spawned process, so the exit code is "
                    + "how a failure gets back. See docs/adr/0001-the-machine-toolchain-is-half-in-process.md."
            );
        result.StdOut.Trim().Should().NotBeEmpty("`dotnet --version` prints a version to stdout");
    }

    [Test]
    public void IsAvailable_is_true_for_a_real_executable()
    {
        new NodeRunner("dotnet").IsAvailable().Should().BeTrue();
    }

    [Test]
    public void IsAvailable_is_false_when_the_executable_is_missing()
    {
        new NodeRunner("trax-no-such-binary-zzz").IsAvailable().Should().BeFalse();
    }

    [Test]
    public void Run_returns_when_the_child_writes_more_than_a_pipe_buffer_to_stderr()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore(
                "Uses sh to write to stderr; the runner's code path is the same on every OS."
            );

        // 200 KB to stderr, then a line to stdout: reading stdout to the end first waits for a child
        // that is itself blocked writing to a full stderr pipe.
        var run = System.Threading.Tasks.Task.Run(() =>
            new NodeRunner("sh").Run(
                "-c",
                ["head -c 200000 /dev/zero | tr '\\0' 'e' 1>&2; echo done"]
            )
        );

        run.Wait(TimeSpan.FromSeconds(30))
            .Should()
            .BeTrue(
                "the runner reads stdout and stderr at once, so a full stderr pipe cannot block it"
            );
        run.Result.ExitCode.Should().Be(0);
        run.Result.StdErr.Length.Should().Be(200000);
        run.Result.StdOut.Trim().Should().Be("done");
    }
}
