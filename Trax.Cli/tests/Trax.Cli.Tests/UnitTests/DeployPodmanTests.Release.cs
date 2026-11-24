using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Deploy.Kubernetes;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// A release on the podman runtime restarts only the units whose files changed, so a release that changes only the
/// app's images leaves Postgres and RabbitMQ running; the network or the Secrets changing restarts every unit, and a
/// unit the stack no longer has is stopped and removed.
/// </summary>
public partial class DeployPodmanTests
{
    private static PodmanStack RenderAt(string tag, bool rabbit = true) =>
        PodmanTarget.Render(
            File_(),
            "home",
            DeployKubernetesTests.Exports(rabbit),
            tag,
            Dir,
            SecretPath
        );

    [Test]
    public void A_first_release_starts_every_unit()
    {
        var stack = RenderAt(Tag);

        var release = stack.ReleaseFrom(
            new Dictionary<string, string>(StringComparer.Ordinal),
            secretsChanged: true
        );

        release.Restart.Should().Equal(stack.Units);
        release.Removed.Should().BeEmpty();
    }

    [Test]
    public void A_release_of_new_images_leaves_the_database_and_broker_running()
    {
        var before = Files(RenderAt("20261120-120000-aaaaaaaaaaaa"));
        var stack = RenderAt(Tag);

        var release = stack.ReleaseFrom(before, secretsChanged: false);

        release.Restart.Should().NotContain([$"{Stack}-database", $"{Stack}-rabbitmq"]);
        DeployKubernetesCommands
            .InstallHint(stack, Dir, release)
            .Should()
            .Contain($"Unchanged, left running: {Stack}-database, {Stack}-rabbitmq.");
        release
            .Restart.Should()
            .Contain([$"{Stack}-hub", $"{Stack}-worker", $"{Stack}-reports"])
            .And.HaveCount(3);
    }

    [Test]
    public void A_release_with_nothing_changed_restarts_nothing()
    {
        var stack = RenderAt(Tag);

        var release = stack.ReleaseFrom(Files(stack), secretsChanged: false);

        release.Restart.Should().BeEmpty();
        DeployKubernetesCommands
            .InstallHint(stack, Dir, release)
            .Should()
            .NotContain("systemctl --user restart")
            .And.Contain("Nothing changed: every unit keeps running.");
    }

    [Test]
    public void Changed_secrets_restart_every_unit()
    {
        var stack = RenderAt(Tag);

        var release = stack.ReleaseFrom(Files(stack), secretsChanged: true);

        release.Restart.Should().Equal(stack.Units);
    }

    [Test]
    public void A_changed_network_restarts_every_unit()
    {
        var stack = RenderAt(Tag);
        var before = Files(stack);
        before[$"{Stack}.network"] = "[Network]\nNetworkName=other\n";

        stack.ReleaseFrom(before, secretsChanged: false).Restart.Should().Equal(stack.Units);
    }

    [Test]
    public void A_changed_migration_restarts_the_unit_that_runs_it()
    {
        var stack = RenderAt(Tag);
        var before = Files(stack);
        before[$"{Stack}-migrate.yaml"] = "kind: Pod\n";

        var release = stack.ReleaseFrom(before, secretsChanged: false);

        release.Restart.Should().ContainSingle();
        Files(stack)[$"{release.Restart.Single()}.kube"].Should().Contain($"{Stack}-migrate.yaml");
    }

    [Test]
    public void A_unit_the_stack_no_longer_has_is_stopped_and_removed()
    {
        var stack = RenderAt(Tag, rabbit: false);
        var before = Files(RenderAt(Tag, rabbit: true));

        var release = stack.ReleaseFrom(before, secretsChanged: false);

        release.Removed.Should().Equal($"{Stack}-rabbitmq");
        DeployKubernetesCommands
            .InstallHint(stack, Dir, release)
            .Should()
            .Contain(
                $"systemctl --user stop {Stack}-rabbitmq.service && rm ~/.config/containers/systemd/{Stack}-rabbitmq.kube"
            );
    }

    [Test]
    public void The_hint_says_a_restarted_service_is_down_for_a_moment_and_where_to_go_for_none()
    {
        var stack = RenderAt(Tag);

        DeployKubernetesCommands
            .InstallHint(
                stack,
                Dir,
                stack.ReleaseFrom(
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    secretsChanged: true
                )
            )
            .Should()
            .Contain("down for that moment; for no downtime, deploy to a cluster.");
    }
}
