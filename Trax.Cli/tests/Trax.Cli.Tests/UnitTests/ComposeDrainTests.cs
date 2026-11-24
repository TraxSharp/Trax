using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Deploy;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// A compose stack drains the running hosts after the migration and before any service is replaced, with the
/// environment's drain timeout, forced only by <c>TRAX_DRAIN_FORCE</c> at <c>compose up</c>, and ends the drain once
/// every service has started. <c>--force</c> cannot reach a file trax writes and someone else runs, so it says how.
/// </summary>
public class ComposeDrainTests
{
    private static string Compose(TimeSpan? timeout = null) =>
        ComposeTarget
            .Render(
                DeployKubernetesTests.File_(),
                DeployKubernetesTests.Exports(),
                DeployEnvironment.Podman,
                limits: null,
                timeout
            )[0]
            .Content.ReplaceLineEndings("\n");

    [Test]
    public void The_drain_runs_with_the_environments_timeout()
    {
        Compose(TimeSpan.FromMinutes(3))
            .Should()
            .Contain("  drain:\n")
            .And.Contain($"command: [\"{TraxDrain.Argument}\", \"00:03:00\"]");
    }

    [Test]
    public void Every_service_waits_for_the_drain_and_the_resume_waits_for_every_service()
    {
        var compose = Compose();

        foreach (var service in new[] { "hub", "worker", "reports" })
        {
            var block = compose[compose.IndexOf($"\n  {service}:\n", StringComparison.Ordinal)..];
            block = block[..block.IndexOf("restart:", StringComparison.Ordinal)];
            block
                .Should()
                .Contain("      drain:\n        condition: service_completed_successfully");
        }
        var resume = compose[compose.IndexOf("\n  resume:\n", StringComparison.Ordinal)..];
        resume
            .Should()
            .Contain($"command: [\"{TraxDrain.ResumeArgument}\"]")
            .And.Contain("      hub:\n        condition: service_started")
            .And.Contain("      worker:\n        condition: service_started")
            .And.Contain("      reports:\n        condition: service_started");
    }

    [Test]
    public void Force_comes_from_the_environment_at_compose_up_and_defaults_to_off()
    {
        Compose()
            .Should()
            .Contain($"{TraxDrain.ForceVariable}: ${{{TraxDrain.ForceVariable}:-false}}");
    }

    [Test]
    public async Task Force_on_compose_says_how_to_force_the_compose_up()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"trax-compose-force-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var stdout = new StringWriter();
            await DeployCommand.DispatchAsync(
                Path.Combine(dir, DeployFile.FileName),
                target: null,
                environment: null,
                outDir: null,
                build: false,
                new DeployKubernetesTests.Exporter(DeployKubernetesTests.Exports(), dir),
                stdout,
                new StringWriter(),
                CancellationToken.None,
                force: true
            );

            stdout.ToString().Should().Contain("TRAX_DRAIN_FORCE=true");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
