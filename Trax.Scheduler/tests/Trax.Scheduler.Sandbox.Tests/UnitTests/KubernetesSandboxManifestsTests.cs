using k8s.Models;
using Trax.Scheduler.Sandbox.Launcher;
using Trax.Scheduler.Sandbox.Tests.Fakes;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>
/// What a cluster is set up with: a restricted namespace, a launcher that may touch Jobs and read
/// pods only, a sandbox account with no token, a deny-all network policy, and admission policies
/// that refuse a Job or pod not shaped like the launcher's.
///
/// <para>Enforces <c>docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md</c>,
/// and <c>docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md</c> for the run's own output volume.</para>
/// </summary>
[Property("adr", "docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md")]
[Property(
    "adr",
    "docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md"
)]
[TestFixture]
public class KubernetesSandboxManifestsTests
{
    private const string Adr =
        "docs/adr/0020-a-sandbox-is-started-only-by-the-launcher-from-a-fixed-request.md";
    private const string OutputAdr =
        "docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md";

    private const string LauncherImage =
        "ghcr.io/example/launcher@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static List<object> Objects() =>
        KubernetesSandboxManifests
            .Objects(LauncherOptions.Kubernetes(), LauncherImage, "trax-hub", "20Gi")
            .ToList();

    [Test]
    public void Namespace_EnforcesRestricted()
    {
        var ns = Objects().OfType<V1Namespace>().Single();

        ns.Metadata.Name.Should().Be("trax-sandbox");
        ns.Metadata.Labels.Should().Contain("pod-security.kubernetes.io/enforce", "restricted");
    }

    [Test]
    public void Role_IsJobsAndReadOnlyPods_WithNoExecOrLog()
    {
        var role = Objects().OfType<V1Role>().Single();
        var resources = role.Rules.SelectMany(r => r.Resources).ToList();

        resources.Should().BeEquivalentTo("jobs", "pods");
        resources
            .Should()
            .NotContain(
                r => r.Contains('/'),
                "no subresource: pods/exec, pods/log, pods/attach, pods/portforward ({0})",
                Adr
            );
        role.Rules.Single(r => r.Resources.Contains("pods"))
            .Verbs.Should()
            .BeEquivalentTo("get", "list", "watch");
        role.Rules.Single(r => r.Resources.Contains("jobs"))
            .Verbs.Should()
            .NotContain(["update", "patch", "*"]);
    }

    [Test]
    public void SandboxServiceAccount_HasNoToken()
    {
        var account = Objects()
            .OfType<V1ServiceAccount>()
            .Single(a =>
                string.Equals(a.Metadata.Name, "trax-sandbox-runner", StringComparison.Ordinal)
            );

        account.AutomountServiceAccountToken.Should().BeFalse();
        Objects()
            .OfType<V1RoleBinding>()
            .SelectMany(b => b.Subjects)
            .Should()
            .NotContain(
                s => string.Equals(s.Name, "trax-sandbox-runner", StringComparison.Ordinal),
                "the sandbox account is bound to nothing"
            );
    }

    [Test]
    public void SandboxNetworkPolicy_DeniesBothWays()
    {
        var policy = Objects()
            .OfType<V1NetworkPolicy>()
            .Single(p =>
                string.Equals(p.Metadata.Name, "trax-sandbox-deny-all", StringComparison.Ordinal)
            );

        policy.Spec.PolicyTypes.Should().BeEquivalentTo("Ingress", "Egress");
        policy.Spec.Ingress.Should().BeNull();
        policy.Spec.Egress.Should().BeNull();
        policy
            .Spec.PodSelector.MatchLabels.Should()
            .Contain("app.kubernetes.io/managed-by", "trax-sandbox-launcher");
    }

    [Test]
    public void LauncherNetworkPolicy_AcceptsOnlyTheHubNamespace()
    {
        var policy = Objects()
            .OfType<V1NetworkPolicy>()
            .Single(p =>
                string.Equals(
                    p.Metadata.Name,
                    "trax-sandbox-launcher-ingress",
                    StringComparison.Ordinal
                )
            );

        policy
            .Spec.Ingress.Single()
            .FromProperty.Single()
            .NamespaceSelector.MatchLabels.Should()
            .Contain("kubernetes.io/metadata.name", "trax-hub");
    }

    [Test]
    public void Launcher_IsOneReplicaWithItsKeyFromASecret()
    {
        var deployment = Objects().OfType<V1Deployment>().Single();
        var container = deployment.Spec.Template.Spec.Containers.Single();

        deployment.Spec.Replicas.Should().Be(1);
        container.Image.Should().Be(LauncherImage);
        container
            .Env.Single(e =>
                string.Equals(e.Name, "Trax__SandboxLauncher__SigningKey", StringComparison.Ordinal)
            )
            .ValueFrom.SecretKeyRef.Name.Should()
            .Be(KubernetesSandboxManifests.KeySecretName);
        container
            .Env.Should()
            .Contain(e =>
                string.Equals(e.Name, "NODE_NAME", StringComparison.Ordinal)
                && e.ValueFrom.FieldRef.FieldPath == "spec.nodeName"
            );
        container
            .Env.Should()
            .Contain(e =>
                string.Equals(
                    e.Name,
                    "Trax__SandboxLauncher__Profiles__escape__Image",
                    StringComparison.Ordinal
                )
                && e.Value == LauncherOptions.Image
            );
        container.SecurityContext.AllowPrivilegeEscalation.Should().BeFalse();
    }

    [Test]
    public void AdmissionPolicies_PinRuntimeImagesAccountAndEnvironment()
    {
        var policies = Objects().OfType<V1ValidatingAdmissionPolicy>().ToList();
        var bindings = Objects().OfType<V1ValidatingAdmissionPolicyBinding>().ToList();

        policies
            .Select(p => p.Metadata.Name)
            .Should()
            .BeEquivalentTo("trax-sandbox-jobs", "trax-sandbox-pods");
        bindings
            .Should()
            .OnlyContain(b => b.Spec.ValidationActions.SequenceEqual(new[] { "Deny" }));
        bindings
            .Should()
            .OnlyContain(b =>
                b.Spec.MatchResources.NamespaceSelector.MatchLabels["kubernetes.io/metadata.name"]
                == "trax-sandbox"
            );
        policies.Should().OnlyContain(p => p.Spec.FailurePolicy == "Fail");

        foreach (var policy in policies)
        {
            var expressions = string.Join('\n', policy.Spec.Validations.Select(v => v.Expression));
            expressions.Should().Contain("runtimeClassName == 'kata'");
            expressions.Should().Contain($"c.image in ['{LauncherOptions.Image}']");
            expressions.Should().Contain("automountServiceAccountToken == false");
            expressions.Should().Contain("serviceAccountName == 'trax-sandbox-runner'");
            expressions.Should().Contain("e.name == 'HOME' || e.name == 'TMPDIR'");
            expressions.Should().Contain("hostNetwork");
        }

        string.Join(
                '\n',
                policies
                    .Single(p =>
                        string.Equals(
                            p.Metadata.Name,
                            "trax-sandbox-pods",
                            StringComparison.Ordinal
                        )
                    )
                    .Spec.Validations.Select(v => v.Expression)
            )
            .Should()
            .Contain($"c.image == '{LauncherImage}'");
    }

    [Test]
    public void AdmissionPolicies_AllowExactlyTheOutputVolumeShape()
    {
        foreach (var policy in Objects().OfType<V1ValidatingAdmissionPolicy>())
        {
            var volumes = policy
                .Spec.Validations.Single(v =>
                    v.Expression.Contains("volumes.all(", StringComparison.Ordinal)
                )
                .Expression;

            volumes.Should().Contain($"storageClassName == '{LauncherOptions.StorageClass}'");
            volumes.Should().Contain("accessModes == ['ReadWriteOnce']");
            volumes.Should().Contain("!has(v.ephemeral.volumeClaimTemplate.spec.dataSource)");
            volumes.Should().Contain("!has(v.ephemeral.volumeClaimTemplate.spec.dataSourceRef)");
            volumes.Should().Contain("!has(v.ephemeral.volumeClaimTemplate.spec.volumeName)");
            volumes
                .Should()
                .Contain("compareTo(quantity('2048Mi')) <= 0", "no larger than the disk cap");
            volumes
                .Should()
                .Contain(
                    "claimName == 'trax-sandbox-runs' && has(v.persistentVolumeClaim.readOnly) && v.persistentVolumeClaim.readOnly",
                    "a sandbox mounts the shared volume read-only; " + OutputAdr
                );
            volumes.Should().NotContain("hostPath");
            policy
                .Spec.Variables.Should()
                .ContainSingle(v =>
                    v.Name == "collector"
                    && v.Expression.Contains(LauncherImage, StringComparison.Ordinal)
                );
        }
    }

    [Test]
    public void Launcher_ReadsOnlyItsOutputStorageClass()
    {
        var role = Objects().OfType<V1ClusterRole>().Single();
        var binding = Objects().OfType<V1ClusterRoleBinding>().Single();

        role.Rules.Should().ContainSingle();
        role.Rules[0].Resources.Should().Equal("storageclasses");
        role.Rules[0].ResourceNames.Should().Equal(LauncherOptions.StorageClass);
        role.Rules[0].Verbs.Should().Equal("get");
        binding.RoleRef.Name.Should().Be(role.Metadata.Name);
        binding.Subjects.Single().Name.Should().Be("trax-sandbox-launcher");
    }

    [Test]
    public void Launcher_HasAMemoryLimitSizedFromItsCaps()
    {
        var options = LauncherOptions.Kubernetes();
        var limit = Objects()
            .OfType<V1Deployment>()
            .Single()
            .Spec.Template.Spec.Containers.Single()
            .Resources.Limits["memory"]
            .ToString();
        options.Caps.MaxConcurrent = 1;

        limit.Should().Be("3250Mi", "with the default caps");
        KubernetesSandboxManifests
            .LauncherMemoryMiB(options.Caps)
            .Should()
            .BeLessThan(3250, "fewer concurrent runs need less");
    }

    [Test]
    public void Objects_AdmissionPoliciesComeBeforeAnythingThatMakesPods()
    {
        var objects = Objects();
        var firstPolicy = objects.FindIndex(o => o is V1ValidatingAdmissionPolicy);
        var deployment = objects.FindIndex(o => o is V1Deployment);

        firstPolicy
            .Should()
            .BeLessThan(deployment, "the launcher's own first pod is admitted under the policies");
    }

    [Test]
    public void Launcher_RunsWithTheCapsItWasRenderedFrom()
    {
        var options = LauncherOptions.Kubernetes();
        options.Caps.MaxConcurrent = 7;
        options.Caps.MaxArchiveFiles = 50;
        options.CanaryProbes.Add("10.0.0.9:5432");
        var env = KubernetesSandboxManifests
            .Objects(options, LauncherImage, "trax-hub", "1Gi")
            .OfType<V1Deployment>()
            .Single()
            .Spec.Template.Spec.Containers.Single()
            .Env;

        env.Should()
            .Contain(e => e.Name == "Trax__SandboxLauncher__Caps__MaxConcurrent" && e.Value == "7");
        env.Should()
            .Contain(e =>
                e.Name == "Trax__SandboxLauncher__Caps__MaxArchiveFiles" && e.Value == "50"
            );
        env.Should()
            .Contain(e =>
                e.Name == "Trax__SandboxLauncher__CanaryProbes__0" && e.Value == "10.0.0.9:5432"
            );
        env.Select(e => e.Name).Should().Contain("Trax__SandboxLauncher__Kubernetes__PollInterval");
        env.Select(e => e.Name)
            .Should()
            .Contain("Trax__SandboxLauncher__Kubernetes__RunsMinFreePercent");
        env.Should()
            .Contain(e =>
                e.Name == "Trax__SandboxLauncher__Kubernetes__OutputStorageClassName"
                && e.Value == LauncherOptions.StorageClass
            );
        env.Should()
            .Contain(e =>
                e.Name == "Trax__SandboxLauncher__Kubernetes__CollectorImage"
                && e.Value == LauncherImage
            );
    }

    [Test]
    public void Launcher_HasAWritableTmpOnItsReadOnlyRoot()
    {
        var pod = Objects().OfType<V1Deployment>().Single().Spec.Template.Spec;

        pod.Containers.Single().VolumeMounts.Should().Contain(m => m.MountPath == "/tmp");
        pod.Volumes.Should().Contain(v => v.EmptyDir != null);
    }

    [Test]
    public void Render_UnpinnedLauncherImage_Throws() =>
        FluentActions
            .Invoking(() =>
                KubernetesSandboxManifests.Render(
                    LauncherOptions.Kubernetes(),
                    "launcher:latest",
                    "trax-hub"
                )
            )
            .Should()
            .Throw<ArgumentException>();

    [Test]
    public void Render_PodmanOptions_Throws() =>
        FluentActions
            .Invoking(() =>
                KubernetesSandboxManifests.Render(
                    LauncherOptions.Podman(),
                    LauncherImage,
                    "trax-hub"
                )
            )
            .Should()
            .Throw<ArgumentException>();

    [Test]
    public void Render_IsOneYamlDocumentPerObject()
    {
        var yaml = KubernetesSandboxManifests.Render(
            LauncherOptions.Kubernetes(),
            LauncherImage,
            "trax-hub"
        );

        yaml.Split("---\n", StringSplitOptions.RemoveEmptyEntries)
            .Should()
            .HaveCount(Objects().Count);
        yaml.Should().Contain("kind: ValidatingAdmissionPolicy").And.Contain("kind: Role\n");
    }
}
