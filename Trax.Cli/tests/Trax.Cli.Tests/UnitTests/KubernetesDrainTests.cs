using AwesomeAssertions;
using k8s.Models;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Kubernetes;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// A kubernetes deploy drains the hosts before it rolls them and ends the drain after, and a hub rolls with its next
/// copy beside it: one more copy at most, none unavailable, ready on its health endpoint, a preStop sleep so the
/// Service stops routing first, and a stop grace of the drain's timeout. Two hubs at once are safe only after the
/// drain, which <c>Trax.Cli/docs/adr/0007</c> records.
/// </summary>
[Property("adr", "docs/adr/0007-hubs-roll-with-overlap-after-a-drain.md")]
public class KubernetesDrainTests
{
    private const string Adr =
        "see cli/0007 (docs/adr/0007-hubs-roll-with-overlap-after-a-drain.md)";
    private const string Tag = "20261124-120000-abc";

    private static KubernetesManifests Render(DeployFile? file = null, bool force = false) =>
        KubernetesTarget.Render(
            file ?? DeployKubernetesTests.File_(),
            "staging",
            DeployKubernetesTests.Exports(),
            Tag,
            eks: null,
            force
        );

    private static V1Deployment Deployment(KubernetesManifests m, string name) =>
        m
            .Services.OfType<V1Deployment>()
            .Single(d => string.Equals(d.Metadata.Name, name, StringComparison.Ordinal));

    private static IList<V1EnvVar> Env(V1Job job) => job.Spec.Template.Spec.Containers[0].Env;

    private static bool Has(IList<V1EnvVar> env, string name, string? value = null) =>
        env.Any(e =>
            string.Equals(e.Name, name, StringComparison.Ordinal)
            && (value is null || string.Equals(e.Value, value, StringComparison.Ordinal))
        );

    [Test]
    public void A_hub_rolls_with_one_new_copy_beside_the_old_and_none_unavailable()
    {
        var hub = Deployment(Render(), "hub");

        hub.Spec.Strategy.Type.Should().Be("RollingUpdate", Adr);
        hub.Spec.Strategy.RollingUpdate.NotNull().MaxSurge.Value.Should().Be("1");
        hub.Spec.Strategy.RollingUpdate.NotNull().MaxUnavailable.Value.Should().Be("0", Adr);
        var container = hub.Spec.Template.Spec.Containers[0];
        container
            .ReadinessProbe.NotNull()
            .HttpGet.NotNull()
            .Path.Should()
            .Be("/trax/health", "the Service routes to the new hub only once it is ready");
        container
            .Lifecycle.NotNull()
            .PreStop.NotNull()
            .Sleep.NotNull()
            .Seconds.Should()
            .Be(
                KubernetesTarget.PreStopSeconds,
                "the Service stops routing before the old hub stops"
            );
        container.Lifecycle.PreStop.Exec.Should().BeNull("the image needs no shell for a sleep");
        hub.Spec.Template.Spec.TerminationGracePeriodSeconds.Should()
            .Be(600 + KubernetesTarget.PreStopSeconds, "the stop allows the drain's timeout");
    }

    [Test]
    public void A_worker_gets_the_stop_grace_and_no_preStop()
    {
        var worker = Deployment(Render(), "worker");

        worker.Spec.Strategy.RollingUpdate.Should().BeNull("a worker keeps the default roll");
        worker
            .Spec.Template.Spec.Containers[0]
            .Lifecycle.Should()
            .BeNull("no Service routes to it");
        worker.Spec.Template.Spec.TerminationGracePeriodSeconds.Should().Be(610);
    }

    [Test]
    public void The_drain_and_resume_Jobs_run_the_migrators_image_once_without_migrating()
    {
        var m = Render();

        m.Drain.Metadata.Name.Should().Be($"orders-drain-{Tag}");
        m.Resume.Metadata.Name.Should().Be($"orders-resume-{Tag}");
        foreach (var job in new[] { m.Drain, m.Resume })
        {
            job.Spec.BackoffLimit.Should().Be(0, "a retry would hide why the first try failed");
            job.Spec.Template.Spec.RestartPolicy.Should().Be("Never");
            job.Spec.Template.Spec.Containers[0]
                .Image.Should()
                .Be(m.Migrate.Spec.Template.Spec.Containers[0].Image);
            Has(Env(job), TraxMigrate.Variable, TraxMigrate.SkipValue).Should().BeTrue();
        }
        m.Drain.Spec.Template.Spec.Containers[0]
            .Args.Should()
            .Equal(TraxDrain.Argument, "00:10:00");
        m.Resume.Spec.Template.Spec.Containers[0].Args.Should().Equal(TraxDrain.ResumeArgument);
        Has(Env(m.Drain), TraxDrain.ForceVariable).Should().BeFalse();
        m.All.Should()
            .ContainInOrder(
                [m.Migrate, m.Drain, Deployment(m, "hub"), m.Resume],
                "the plan lists them in the order the deploy runs them"
            );
    }

    [Test]
    public void Force_lets_only_the_drain_Job_roll_past_its_timeout()
    {
        var m = Render(force: true);

        Has(Env(m.Drain), TraxDrain.ForceVariable, "true").Should().BeTrue();
        Has(Env(m.Resume), TraxDrain.ForceVariable).Should().BeFalse();
        Has(Env(m.Migrate), TraxDrain.ForceVariable).Should().BeFalse();
    }

    [Test]
    public void The_environments_drain_timeout_is_the_drains_argument_and_the_stop_grace()
    {
        var file = DeployKubernetesTests.File_();
        file.Environments["staging"].DrainTimeout = "00:02:30";

        var m = Render(file);

        m.Drain.Spec.Template.Spec.Containers[0]
            .Args.Should()
            .Equal(TraxDrain.Argument, "00:02:30");
        Deployment(m, "hub").Spec.Template.Spec.TerminationGracePeriodSeconds.Should().Be(160);
    }

    [TestCase("ten minutes")]
    [TestCase("00:00:00")]
    [TestCase("2.00:00:00")]
    [TestCase("-00:10:00")]
    public void A_drain_timeout_that_is_not_one_second_to_a_day_is_refused(string timeout)
    {
        var file = DeployKubernetesTests.File_();
        file.Environments["staging"].DrainTimeout = timeout;

        var act = () => file.Environment("staging");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"Environment 'staging': drainTimeout '{timeout}'*00:10:00*");
    }

    [Test]
    public void No_drain_timeout_means_ten_minutes()
    {
        DeployKubernetesTests
            .File_()
            .Environment("staging")
            .DrainTimeoutValue()
            .Should()
            .Be(TimeSpan.FromMinutes(10));
    }

    private static (
        KubernetesDeployer,
        KubernetesDeployerTests.FakeCluster,
        List<string>
    ) Deployer()
    {
        var cluster = new KubernetesDeployerTests.FakeCluster
        {
            SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"],
        };
        var said = new List<string>();
        return (
            new KubernetesDeployer(cluster, new KubernetesDeployerTests.FakeImages(), said.Add),
            cluster,
            said
        );
    }

    private static void Running(KubernetesDeployerTests.FakeCluster cluster)
    {
        foreach (var name in new[] { "hub", "worker", "reports" })
            cluster.Deployments[name] = new V1Deployment
            {
                ApiVersion = "apps/v1",
                Kind = "Deployment",
                Metadata = new V1ObjectMeta { Name = name },
                Spec = new V1DeploymentSpec(),
            };
    }

    private static Task Deploy(KubernetesDeployer deployer, bool force = false) =>
        deployer.DeployAsync(
            DeployKubernetesTests.File_(),
            "/src/trax.deploy.json",
            "staging",
            DeployKubernetesTests.Exports(),
            Tag,
            CancellationToken.None,
            eks: null,
            force
        );

    [Test]
    public async Task A_first_deploy_has_nothing_to_drain()
    {
        var (deployer, cluster, _) = Deployer();

        await Deploy(deployer);

        cluster.Calls.Should().NotContain(c => c.Contains("-drain-", StringComparison.Ordinal));
        cluster.Calls.Should().NotContain(c => c.Contains("-resume-", StringComparison.Ordinal));
    }

    [Test]
    public async Task A_deploy_over_running_services_drains_after_migrating_rolls_then_resumes()
    {
        var (deployer, cluster, _) = Deployer();
        Running(cluster);

        await Deploy(deployer);

        var calls = cluster.Calls;
        int At(string call) => calls.IndexOf(call);
        At($"wait-job orders-migrate-{Tag}")
            .Should()
            .BeLessThan(At($"create-job orders-drain-{Tag}"));
        At($"wait-job orders-drain-{Tag}")
            .Should()
            .BeLessThan(At("apply Deployment/hub"), "nothing rolls before the drain ends, " + Adr);
        At("wait Deployment/worker").Should().BeLessThan(At($"create-job orders-resume-{Tag}"));
        calls[^1].Should().Be($"wait-job orders-resume-{Tag}");
        cluster
            .JobTimeouts[$"orders-drain-{Tag}"]
            .Should()
            .Be(TimeSpan.FromMinutes(10) + KubernetesDeployer.DrainJobSlack);
    }

    [Test]
    public async Task A_drain_with_work_still_busy_refuses_naming_it_and_moves_no_service()
    {
        var (deployer, cluster, _) = Deployer();
        Running(cluster);
        cluster.JobResults["-drain-"] = new KubernetesJobResult(
            Succeeded: false,
            "BackoffLimitExceeded",
            "  run 42 Orders.IBillTrain (InProgress, since 2026-11-24 12:00:00Z, on hub-7f)"
        );

        var act = () => Deploy(deployer);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "The drain failed (BackoffLimitExceeded), so no service was moved*run 42 Orders.IBillTrain*"
                + "*trax deploy --env staging --force*"
        );
        cluster
            .Calls.Should()
            .NotContain(c => c.StartsWith("apply Deployment", StringComparison.Ordinal));
        cluster
            .Calls.Should()
            .NotContain(
                c => c.Contains("-resume-", StringComparison.Ordinal),
                "the drain ended itself when it refused"
            );
    }

    [Test]
    public async Task Force_reaches_the_drain_Job_the_deploy_runs()
    {
        var (deployer, cluster, _) = Deployer();
        Running(cluster);

        await Deploy(deployer, force: true);

        Has(
                Env(
                    cluster.Jobs.Single(j =>
                        j.Metadata.Name.Contains("-drain-", StringComparison.Ordinal)
                    )
                ),
                TraxDrain.ForceVariable,
                "true"
            )
            .Should()
            .BeTrue();
    }

    [Test]
    public async Task A_roll_that_fails_after_the_drain_is_rolled_back_and_the_drain_ended()
    {
        var (deployer, cluster, _) = Deployer();
        Running(cluster);
        cluster.FailRollout = "Deployment/hub";

        var act = () => Deploy(deployer);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var calls = cluster.Calls;
        calls
            .LastIndexOf("wait Deployment/hub")
            .Should()
            .BeLessThan(
                calls.IndexOf($"create-job orders-resume-{Tag}"),
                "the rollback comes first"
            );
        calls[^1].Should().Be($"wait-job orders-resume-{Tag}");
    }

    [Test]
    public async Task A_cluster_error_during_the_roll_still_rolls_back_and_ends_the_drain()
    {
        var (deployer, cluster, _) = Deployer();
        Running(cluster);
        var failed = false;
        cluster.OnWait = target =>
        {
            if (!failed && string.Equals(target, "Deployment/hub", StringComparison.Ordinal))
            {
                failed = true;
                throw new HttpRequestException("the connection was reset");
            }
            return Task.CompletedTask;
        };

        var act = () => Deploy(deployer);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage(
            "the connection was reset Every service was rolled back*"
        );
        cluster
            .Calls[^1]
            .Should()
            .Be($"wait-job orders-resume-{Tag}", "the hosts are not left drained");
    }

    [Test]
    public async Task A_resume_Job_the_API_refuses_leaves_the_deploy_done_and_says_the_drain_lapses()
    {
        var (deployer, cluster, said) = Deployer();
        Running(cluster);
        cluster.OnCreateJob = job =>
        {
            if (job.Metadata.Name.Contains("-resume-", StringComparison.Ordinal))
                throw new HttpRequestException("the API server is unavailable");
        };

        await Deploy(deployer);

        said.Should()
            .Contain(s =>
                s.Contains(
                    "Ending the drain failed (the API server is unavailable)",
                    StringComparison.Ordinal
                )
            );
    }

    [Test]
    public async Task A_resume_that_fails_leaves_the_deploy_done_and_says_the_drain_lapses()
    {
        var (deployer, cluster, said) = Deployer();
        Running(cluster);
        cluster.JobResults["-resume-"] = new KubernetesJobResult(
            Succeeded: false,
            "DeadlineExceeded",
            ""
        );

        await Deploy(deployer);

        said.Should()
            .Contain(s =>
                s.Contains("Ending the drain failed (DeadlineExceeded)", StringComparison.Ordinal)
                && s.Contains("lapses on its own", StringComparison.Ordinal)
            );
    }

    [Test]
    public void A_podman_hub_keeps_no_preStop()
    {
        var stack = PodmanTarget.Render(
            DeployPodmanTests.File_(),
            "home",
            DeployKubernetesTests.Exports(),
            Tag,
            "/src/.trax/deploy/podman/orders-home",
            "/home/u/.config/trax/orders-home/secret.yaml"
        );

        stack
            .Files.Single(f =>
                string.Equals(f.Path, "orders-home-hub.yaml", StringComparison.Ordinal)
            )
            .Content.Should()
            .NotContain(
                "preStop",
                "a unit's restart stops the pod outright; no Service routes to it"
            );
    }
}
