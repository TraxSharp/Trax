using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Deploy.Kubernetes;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// On the podman runtime systemd stops a unit before anything in it runs, so the drain is a pod the install runs before
/// it restarts the app's units: the restart is chained to the drain exiting 0, and the resume pod runs after. A first
/// release, and one that restarts only Postgres or RabbitMQ or nothing, does not drain.
/// </summary>
[Property("adr", "docs/adr/0007-hubs-roll-with-overlap-after-a-drain.md")]
public class PodmanDrainTests
{
    private const string Adr =
        "see cli/0007 (docs/adr/0007-hubs-roll-with-overlap-after-a-drain.md)";
    private const string Tag = "20261124-120000-abcdef123456";
    private const string Dir = "/src/.trax/deploy/podman/orders-home";
    private const string SecretPath = "/home/u/.config/trax/orders-home/secret.yaml";

    private static PodmanStack Render(string tag = Tag, bool force = false) =>
        PodmanTarget.Render(
            DeployPodmanTests.File_(),
            "home",
            DeployKubernetesTests.Exports(),
            tag,
            Dir,
            SecretPath,
            force
        );

    private static Dictionary<string, string> Files(PodmanStack stack) =>
        stack.Files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);

    [Test]
    public void The_drain_and_resume_pods_are_written_beside_the_migrate_pod()
    {
        var files = Files(Render());

        files["orders-home-drain.yaml"]
            .Should()
            .Contain(TraxDrain.Argument, Adr)
            .And.Contain("00:10:00")
            .And.Contain("name: orders-home-drain")
            .And.NotContain(TraxDrain.ForceVariable);
        files["orders-home-resume.yaml"].Should().Contain(TraxDrain.ResumeArgument);
        files.Keys.Should().NotContain(k => k.EndsWith("drain.kube", StringComparison.Ordinal));
    }

    [Test]
    public void Force_is_written_into_the_drain_pod()
    {
        Files(Render(force: true))["orders-home-drain.yaml"]
            .Should()
            .Contain(TraxDrain.ForceVariable);
    }

    [Test]
    public void A_release_of_new_images_restarts_only_after_the_drain_exits_0_then_resumes()
    {
        var before = Files(Render("20261120-120000-aaaaaaaaaaaa"));
        var stack = Render();
        var release = stack.ReleaseFrom(before, secretsChanged: false);

        release.Drain.Should().BeTrue();
        var lines = DeployKubernetesCommands.InstallHint(stack, Dir, release).Split('\n');
        var drain = lines.Single(l =>
            l.Contains("orders-home-drain.yaml", StringComparison.Ordinal)
        );
        drain
            .Should()
            .StartWith($"  {PodmanTarget.Podman} kube play --replace {SecretPath} && ")
            .And.Contain(
                "test \"$(/usr/bin/podman wait orders-home-drain-pod-drain)\" = 0 && systemctl --user restart "
            );
        var resume = Array.FindIndex(
            lines,
            l => l.Contains("orders-home-resume.yaml", StringComparison.Ordinal)
        );
        resume.Should().BeGreaterThan(Array.IndexOf(lines, drain));
        drain
            .Should()
            .EndWith("; /usr/bin/podman pod stop --ignore -t 10 orders-home-drain-pod >/dev/null");
        lines[resume]
            .Should()
            .EndWith(
                "&& /usr/bin/podman wait orders-home-resume-pod-resume >/dev/null; "
                    + "/usr/bin/podman pod stop --ignore -t 10 orders-home-resume-pod >/dev/null",
                "a pod held to egress keeps its sidecar serving until it is stopped"
            );
        lines
            .Should()
            .NotContain(
                l => l.StartsWith("  systemctl --user restart", StringComparison.Ordinal),
                "the restart runs only through the drain"
            );
    }

    [Test]
    public void A_service_pod_carries_no_probe_podman_would_run_as_curl()
    {
        // podman turns an httpGet probe into curl inside the container, which the image lacks: the liveness probe
        // would restart the hub every few dozen seconds, and with it its startup recovery, mid-drain.
        Files(Render())["orders-home-hub.yaml"]
            .Should()
            .NotContain("livenessProbe")
            .And.NotContain("readinessProbe");
    }

    [Test]
    public void A_first_release_does_not_drain()
    {
        var stack = Render();
        var release = stack.ReleaseFrom(
            new Dictionary<string, string>(StringComparer.Ordinal),
            secretsChanged: true
        );

        release.Drain.Should().BeFalse();
        DeployKubernetesCommands.InstallHint(stack, Dir, release).Should().NotContain("drain");
    }

    [Test]
    public void A_release_that_changes_nothing_does_not_drain()
    {
        var stack = Render();

        stack.ReleaseFrom(Files(stack), secretsChanged: false).Drain.Should().BeFalse();
    }

    [Test]
    public void Changed_secrets_restart_every_unit_after_a_drain()
    {
        var stack = Render();

        stack.ReleaseFrom(Files(stack), secretsChanged: true).Drain.Should().BeTrue();
    }
}
