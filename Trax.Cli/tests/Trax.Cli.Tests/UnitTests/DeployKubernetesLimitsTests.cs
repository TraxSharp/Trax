using AwesomeAssertions;
using k8s.Models;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Kubernetes;
using Trax.Core.Extensions;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// A kubernetes environment renders the memory and CPU each service names as its request and limit, leaves both off a
/// service that names neither, applies <c>limits</c> to the Postgres and RabbitMQ it runs in the cluster, and refuses
/// what a cluster cannot apply: a pids limit, a database limit when the database is not in the cluster, and a broker
/// limit when no host uses RabbitMQ.
/// </summary>
public class DeployKubernetesLimitsTests
{
    private const string Tag = "20261124-120000-abcdef123456";

    private static Dictionary<string, V1Container> Containers(KubernetesManifests m) =>
        m
            .All.Select(o =>
                o switch
                {
                    V1Deployment d => d.Spec.Template.Spec.Containers.Single(),
                    V1StatefulSet s => s.Spec.Template.Spec.Containers.Single(),
                    V1Job j => j.Spec.Template.Spec.Containers.Single(),
                    _ => null,
                }
            )
            .OfType<V1Container>()
            .ToDictionary(c => c.Name, StringComparer.Ordinal);

    [Test]
    public void A_service_that_names_no_memory_or_cpu_has_no_request_or_limit()
    {
        var file = DeployKubernetesTests.File_();
        var m = KubernetesTarget.Render(file, "staging", DeployKubernetesTests.Exports(), Tag);

        var containers = Containers(m);
        containers["hub"].Resources.Should().BeNull();
        containers["reports"].Resources.Should().BeNull();
        containers["worker"].Resources.Limits.Should().ContainKey("memory");
        containers["worker"].Resources.Limits.Should().NotContainKey("cpu");
    }

    [Test]
    public void The_cpus_a_service_names_are_its_cpu_request_and_limit_in_millicores()
    {
        var file = DeployKubernetesTests.File_();
        file.Services["hub"].Cpus = 1.5m;

        var hub = Containers(
            KubernetesTarget.Render(file, "staging", DeployKubernetesTests.Exports(), Tag)
        )["hub"];

        hub.Resources.Limits["cpu"].ToString().Should().Be("1500m");
        hub.Resources.Requests["cpu"].ToString().Should().Be("1500m");
        hub.Resources.Limits.Should().NotContainKey("memory");
    }

    [Test]
    public void The_environment_sets_the_in_cluster_database_and_broker_limits()
    {
        var file = DeployKubernetesTests.File_();
        var env = file.Environments["staging"];
        env.Limits["database"] = new DeployLimits { Memory = 3072, Cpus = 2 };
        env.Limits["rabbitmq"] = new DeployLimits { Memory = 768 };

        var containers = Containers(
            KubernetesTarget.Render(
                file,
                "staging",
                DeployKubernetesTests.Exports(rabbit: true),
                Tag
            )
        );

        containers["database"].Resources.Limits["memory"].ToString().Should().Be("3Gi");
        containers["database"].Resources.Limits["cpu"].ToString().Should().Be("2");
        containers["rabbitmq"].Resources.Limits["memory"].ToString().Should().Be("768Mi");
    }

    [Test]
    public void The_backing_services_keep_their_measured_memory_when_the_environment_names_none()
    {
        var containers = Containers(
            KubernetesTarget.Render(
                DeployKubernetesTests.File_(),
                "staging",
                DeployKubernetesTests.Exports(rabbit: true),
                Tag
            )
        );

        containers["database"].Resources.Limits["memory"].ToString().Should().Be("1Gi");
        containers["rabbitmq"].Resources.Limits["memory"].ToString().Should().Be("512Mi");
    }

    [Test]
    public void A_service_that_names_pids_is_refused_on_kubernetes()
    {
        var file = DeployKubernetesTests.File_();
        file.Services["worker"].Pids = 1024;

        var act = () => file.Environment("staging");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*service 'worker' names pids, which kubernetes cannot apply*podPidsLimit*"
            );
    }

    [Test]
    public void A_backing_pids_limit_is_refused_on_kubernetes()
    {
        var file = DeployKubernetesTests.File_();
        file.Environments["staging"].Limits["database"] = new DeployLimits { Pids = 1024 };

        var act = () => file.Environment("staging");

        act.Should().Throw<InvalidOperationException>().WithMessage("*limits.database names pids*");
    }

    [Test]
    public void A_database_limit_is_refused_when_the_database_is_not_in_the_cluster()
    {
        var file = DeployKubernetesTests.File_(DeployEnvironment.Production);
        file.Environments["staging"].Limits["database"] = new DeployLimits { Memory = 2048 };

        var act = () => file.Environment("staging");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*limits.database is for a database trax runs in the cluster*");
    }

    [Test]
    public void A_broker_limit_is_refused_when_no_host_uses_rabbitmq()
    {
        var file = DeployKubernetesTests.File_();
        file.Environments["staging"].Limits["rabbitmq"] = new DeployLimits { Memory = 768 };

        var act = () =>
            KubernetesTarget.Render(file, "staging", DeployKubernetesTests.Exports(), Tag);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*limits names rabbitmq, and no host uses RabbitMQ*");
    }

    [TestCase("redis", "*limits names 'redis', which is not a service trax runs beside the app*")]
    [TestCase("hub", "*limits names 'hub'*its own entry under services*")]
    public void A_limit_on_anything_but_the_database_or_broker_is_refused(
        string name,
        string message
    )
    {
        var file = DeployKubernetesTests.File_();
        file.Environments["staging"].Limits[name] = new DeployLimits { Memory = 768 };

        var act = () => file.Environment("staging");

        act.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [Test]
    public void A_backing_limit_out_of_range_is_refused_naming_it()
    {
        var file = DeployKubernetesTests.File_();
        file.Environments["staging"].Limits["database"] = new DeployLimits { Memory = 64 };

        var act = () => file.Environment("staging");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*limits.database has memory 64;*at least 128 (MiB)*");
    }

    [Test]
    public void The_scratch_volume_is_empty_and_only_on_pods_with_a_read_only_root()
    {
        var m = KubernetesTarget.Render(
            DeployKubernetesTests.File_(),
            "staging",
            DeployKubernetesTests.Exports(rabbit: true),
            Tag
        );

        var database = m.Backing.OfType<V1StatefulSet>().Single();
        (database.Spec.Template.Spec.Volumes ?? []).Should().BeEmpty();
        var hub = m
            .Services.OfType<V1Deployment>()
            .Single(d => string.Equals(d.Metadata.Name, "hub", StringComparison.Ordinal));
        hub.Spec.Template.Spec.Volumes.NotNull()
            .Should()
            .ContainSingle()
            .Which.EmptyDir.Should()
            .NotBeNull();
    }

    [Test]
    public void Every_service_has_a_progress_deadline_the_deploy_reads()
    {
        var m = KubernetesTarget.Render(
            DeployKubernetesTests.File_(),
            "staging",
            DeployKubernetesTests.Exports(),
            Tag
        );

        foreach (var deployment in m.Services.OfType<V1Deployment>())
            deployment
                .Spec.ProgressDeadlineSeconds.Should()
                .Be(KubernetesTarget.RolloutProgressDeadlineSeconds, deployment.Metadata.Name);
    }
}
