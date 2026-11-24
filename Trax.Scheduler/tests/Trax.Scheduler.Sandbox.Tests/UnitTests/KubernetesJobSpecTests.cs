using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Sandbox.Tests.Fakes;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>
/// A sandbox Job on a cluster: the RuntimeClass, no token, no host namespaces, restricted
/// security, the declared limits, no network resolver, and the run's own directories only.
///
/// <para>Enforces <c>docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md")]
[TestFixture]
public class KubernetesJobSpecTests
{
    private const string Adr =
        "docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md";

    private const string RunId = "0123456789abcdef0123456789abcdef";

    private static k8s.Models.V1Job Build()
    {
        var options = LauncherOptions.Kubernetes();
        var job = new SandboxJob(
            RunId,
            options.Profiles["escape"],
            new SandboxLimits(1500, 512, 64, 300),
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
        );
        return KubernetesJobSpec.Build(job, options.Kubernetes);
    }

    [Test]
    public void Build_PodIsPinnedAndTokenless()
    {
        var pod = Build().Spec.Template.Spec;

        pod.RuntimeClassName.Should().Be("kata", Adr);
        pod.AutomountServiceAccountToken.Should().BeFalse();
        pod.ServiceAccountName.Should().Be("trax-sandbox-runner");
        pod.EnableServiceLinks.Should().BeFalse();
        pod.HostNetwork.Should().BeFalse();
        pod.HostPID.Should().BeFalse();
        pod.HostIPC.Should().BeFalse();
        pod.RestartPolicy.Should().Be("Never");
        pod.DnsPolicy.Should().Be("None");
        pod.SecurityContext.RunAsNonRoot.Should().BeTrue();
        pod.SecurityContext.SeccompProfile.Type.Should().Be("RuntimeDefault");
        pod.InitContainers.Should().BeNull();
        pod.Affinity.NodeAffinity.RequiredDuringSchedulingIgnoredDuringExecution.NodeSelectorTerms.Single()
            .MatchFields.Single()
            .Values.Should()
            .Equal("node-a");
    }

    [Test]
    public void Build_ContainerIsRestrictedAndLimited()
    {
        var container = Build().Spec.Template.Spec.Containers.Single();

        container.Image.Should().Be(LauncherOptions.Image);
        container.SecurityContext.Privileged.Should().BeFalse();
        container.SecurityContext.AllowPrivilegeEscalation.Should().BeFalse();
        container.SecurityContext.ReadOnlyRootFilesystem.Should().BeTrue();
        container.SecurityContext.Capabilities.Drop.Should().Equal("ALL");
        container.SecurityContext.Capabilities.Add.Should().BeNull();
        container.SecurityContext.RunAsUser.Should().Be(65532);
        container.Resources.Limits["cpu"].ToString().Should().Be("1500m");
        container.Resources.Limits["memory"].ToString().Should().Be("512Mi");
        container.Resources.Limits["ephemeral-storage"].ToString().Should().Be("64Mi");
        container.Env.Select(e => e.Name).Should().Equal("HOME", "TMPDIR");
        container.EnvFrom.Should().BeNull();
    }

    [Test]
    public void Build_MountsOnlyTheRunsOwnDirectories()
    {
        var spec = Build().Spec.Template.Spec;
        var mounts = spec.Containers.Single().VolumeMounts;

        mounts
            .Should()
            .ContainSingle(m =>
                m.MountPath == "/sandbox/in"
                && m.SubPath == $"{RunId}/in"
                && m.ReadOnlyProperty == true
            );
        mounts
            .Should()
            .ContainSingle(m =>
                m.MountPath == "/sandbox/out"
                && m.SubPath == $"{RunId}/out"
                && m.ReadOnlyProperty != true
            );
        spec.Volumes.Should().HaveCount(2);
        spec.Volumes.Should()
            .ContainSingle(v =>
                v.PersistentVolumeClaim != null
                && v.PersistentVolumeClaim.ClaimName == "trax-sandbox-runs"
            );
        spec.Volumes.Should()
            .ContainSingle(v => v.EmptyDir != null && v.EmptyDir.Medium == "Memory");
        spec.Volumes.Should()
            .NotContain(v => v.HostPath != null || v.Projected != null || v.Secret != null);
    }

    [Test]
    public void Build_JobDiesAtItsTimeoutAndIsNotRetried()
    {
        var job = Build();

        job.Spec.ActiveDeadlineSeconds.Should().Be(300);
        job.Spec.BackoffLimit.Should().Be(0);
        job.Metadata.Name.Should().Be($"trax-sbx-{RunId}");
        job.Metadata.NamespaceProperty.Should().Be("trax-sandbox");
        job.Spec.Template.Metadata.Labels.Should()
            .ContainKey("app.kubernetes.io/managed-by")
            .And.NotContainKey("app.kubernetes.io/name");
    }

    [TestCase("Complete", null, "Complete")]
    [TestCase("Failed", "DeadlineExceeded", "DeadlineExceeded")]
    [TestCase("Failed", "BackoffLimitExceeded", "Failed")]
    public void Ended_ReadsTheJobsCondition(string type, string? reason, string ended)
    {
        var job = new k8s.Models.V1Job
        {
            Status = new k8s.Models.V1JobStatus
            {
                Conditions =
                [
                    new k8s.Models.V1JobCondition
                    {
                        Type = type,
                        Status = "True",
                        Reason = reason,
                    },
                ],
            },
        };

        KubernetesSandboxBackend.Ended(job).Should().Be(ended);
    }

    [Test]
    public void Ended_Running_IsNull() =>
        KubernetesSandboxBackend
            .Ended(new k8s.Models.V1Job { Status = new k8s.Models.V1JobStatus() })
            .Should()
            .BeNull();
}
