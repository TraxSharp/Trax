using AwesomeAssertions;
using k8s;
using k8s.Models;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Kubernetes;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

public partial class DeployEgressTests
{
    private const string Tag = "20261124-120000-abcdef123456";

    private static KubernetesManifests Cluster(
        DeployFile? file = null,
        bool rabbit = false,
        EksTarget? eks = null
    ) =>
        KubernetesTarget.Render(
            file ?? Held(),
            "staging",
            eks is null ? DeployKubernetesTests.Exports(rabbit) : EksDeployTests.Exports(),
            Tag,
            eks
        );

    private static V1PodSpec PodOf(KubernetesManifests m, string deployment) =>
        m
            .Services.OfType<V1Deployment>()
            .Single(d => string.Equals(d.Metadata.Name, deployment, StringComparison.Ordinal))
            .Spec.Template.Spec;

    private static string Allow(V1Container c) =>
        c
            .Env.Single(e =>
                string.Equals(e.Name, DeployEgress.AllowVariable, StringComparison.Ordinal)
            )
            .Value;

    [Test]
    public void Kubernetes_applies_the_rules_in_an_init_container_with_net_admin_alone()
    {
        var pod = PodOf(Cluster(), "hub");
        var rules = pod.InitContainers[0];

        rules.Name.Should().Be("egress-rules");
        rules.Image.Should().Be(Image);
        rules.Command.Should().BeNull("the policy admits the image's own entrypoint only");
        rules.Args.Should().Equal("rules");
        rules.RestartPolicy.Should().BeNull("it applies the rules and exits");
        rules.SecurityContext.Capabilities.Drop.Should().Equal("ALL");
        rules.SecurityContext.Capabilities.Add.Should().Equal("NET_ADMIN");
        rules.SecurityContext.RunAsUser.Should().Be(0);
        rules.SecurityContext.AllowPrivilegeEscalation.Should().BeFalse();
        rules.SecurityContext.ReadOnlyRootFilesystem.Should().BeTrue();
        rules.SecurityContext.Privileged.Should().BeNull();
    }

    [Test]
    public void Kubernetes_serves_the_resolver_and_proxy_from_a_native_sidecar_with_no_capability()
    {
        var pod = PodOf(Cluster(), "hub");
        var sidecar = pod.InitContainers[1];

        sidecar.Name.Should().Be("egress");
        sidecar.Image.Should().Be(Image);
        sidecar.Args.Should().Equal("serve");
        sidecar
            .RestartPolicy.Should()
            .Be("Always", "a native sidecar starts before the app and runs beside it");
        sidecar.SecurityContext.RunAsUser.Should().Be(DeployEgress.Uid);
        sidecar.SecurityContext.RunAsNonRoot.Should().BeTrue();
        sidecar.SecurityContext.Capabilities.Drop.Should().Equal("ALL");
        sidecar.SecurityContext.Capabilities.Add.Should().BeNull();
        sidecar.StartupProbe.Exec.Command.Should().Equal("/usr/local/bin/trax-egress", "ready");
        Allow(sidecar)
            .Should()
            .Be("api.anthropic.com:443 registry.example.com:8443 tcp:database:5432");
        Allow(pod.InitContainers[0])
            .Should()
            .Be(Allow(sidecar), "the rules and the sidecar read one list");
    }

    [Test]
    public void Kubernetes_leaves_the_app_container_with_every_capability_dropped_and_not_the_egress_uid()
    {
        var app = PodOf(Cluster(), "hub").Containers.Single();

        app.SecurityContext.Capabilities.Drop.Should().Equal("ALL");
        app.SecurityContext.Capabilities.Add.Should().BeNull();
        app.SecurityContext.RunAsUser.Should().NotBe(DeployEgress.Uid);
        app.SecurityContext.AllowPrivilegeEscalation.Should().BeFalse();
    }

    [Test]
    public void Kubernetes_shares_one_process_namespace_in_a_held_pod()
    {
        // The sidecar refuses to serve beside a process of the egress uid, which it sees only when the pod shares
        // one PID namespace.
        var m = Cluster();

        PodOf(m, "hub").ShareProcessNamespace.Should().BeTrue();
        foreach (var job in new[] { m.Migrate, m.Drain, m.Resume })
            job.Spec.Template.Spec.ShareProcessNamespace.Should().BeTrue();
        PodOf(m, "worker").ShareProcessNamespace.Should().BeNull();
    }

    [Test]
    public void Kubernetes_gives_a_service_without_egress_no_init_containers()
    {
        var m = Cluster();

        PodOf(m, "worker").InitContainers.Should().BeNull();
        PodOf(m, "reports").InitContainers.Should().BeNull();
    }

    [Test]
    public void Kubernetes_holds_the_migrate_job_to_the_migrators_egress()
    {
        var job = Cluster().Migrate.Spec.Template;

        job.Spec.InitContainers.Select(c => c.Name).Should().Equal("egress-rules", "egress");
        job.Metadata.Annotations.Should()
            .ContainKey("container.seccomp.security.alpha.kubernetes.io/egress-rules")
            .And.ContainKey("container.seccomp.security.alpha.kubernetes.io/egress");
    }

    [Test]
    public void Kubernetes_holds_the_drain_and_resume_jobs_to_the_migrators_egress()
    {
        // They run the migrator's code as the migration does, and reach the database through the same sidecar.
        var m = Cluster();

        foreach (var job in new[] { m.Drain, m.Resume })
        {
            var pod = job.Spec.Template;
            pod.Spec.InitContainers.Select(c => c.Name).Should().Equal("egress-rules", "egress");
            Allow(pod.Spec.InitContainers[1])
                .Should()
                .Be(Allow(m.Migrate.Spec.Template.Spec.InitContainers[1]));
            pod.Metadata.Annotations.Should()
                .ContainKey("container.seccomp.security.alpha.kubernetes.io/egress");
        }
    }

    [Test]
    public void Kubernetes_allows_the_in_cluster_broker_by_name()
    {
        Allow(PodOf(Cluster(rabbit: true), "hub").InitContainers[1])
            .Should()
            .Be(
                "api.anthropic.com:443 registry.example.com:8443 tcp:database:5432 tcp:rabbitmq:5672"
            );
    }

    [Test]
    public void Kubernetes_allows_an_existing_database_by_the_hosts_in_its_secret()
    {
        var file = Held(DeployEnvironment.Production);

        var sidecar = PodOf(Cluster(file), "hub").InitContainers[1];

        Allow(sidecar).Should().Be("api.anthropic.com:443 registry.example.com:8443");
        var postgres = sidecar.Env.Single(e =>
            string.Equals(e.Name, DeployEgress.PostgresVariable, StringComparison.Ordinal)
        );
        postgres
            .Value.Should()
            .BeNull("the connection string is read from its Secret, never written");
        postgres.ValueFrom.SecretKeyRef.Name.Should().Be("orders-db");
        postgres.ValueFrom.SecretKeyRef.Key.Should().Be("connection");
    }

    [Test]
    public void Kubernetes_on_eks_allows_the_foundations_database_pod_identity_and_secrets_manager()
    {
        var file = EksDeployTests.File_();
        file.EgressImage = Image;
        file.Services["hub"].Egress = ["api.anthropic.com"];
        var eks = new EksTarget(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["hub"] = "123.dkr.ecr.us-east-1.amazonaws.com/hub:1",
                ["worker"] = "123.dkr.ecr.us-east-1.amazonaws.com/worker:1",
                ["reports"] = "123.dkr.ecr.us-east-1.amazonaws.com/reports:1",
            },
            "us-east-1",
            "db.abc.us-east-1.rds.amazonaws.com",
            "5432",
            "arn:db",
            "sg-client",
            "sg-cluster",
            PodSecurityGroups: true
        );

        var allow = Allow(PodOf(Cluster(file, eks: eks), "hub").InitContainers[1]);

        allow
            .Should()
            .Be(
                "api.anthropic.com:443 tcp:169.254.170.23:80 tcp:db.abc.us-east-1.rds.amazonaws.com:5432 "
                    + "tcp:secretsmanager.us-east-1.amazonaws.com:443"
            );
        allow.Should().NotContain("169.254.169.254", "instance metadata stays out of reach on EKS");
    }

    [Test]
    public void Kubernetes_renders_an_admission_policy_bound_to_the_namespace_alone()
    {
        var m = Cluster();

        m.Admission.Select(o => o.Kind)
            .Should()
            .Equal("ValidatingAdmissionPolicy", "ValidatingAdmissionPolicyBinding");
        var binding = m.Admission.OfType<V1ValidatingAdmissionPolicyBinding>().Single();
        binding.Metadata.Name.Should().Be("trax-egress-orders-staging");
        binding.Spec.PolicyName.Should().Be("trax-egress-orders-staging");
        binding.Spec.ValidationActions.Should().Equal("Deny");
        binding
            .Spec.MatchResources.NamespaceSelector.MatchLabels.Should()
            .Equal(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["kubernetes.io/metadata.name"] = "orders-staging",
                }
            );
        foreach (var o in m.Admission)
            o.Metadata.Labels[KubernetesTarget.AppLabel].Should().Be("orders");
    }

    [Test]
    public void The_policy_lets_only_the_rules_container_of_the_released_image_add_net_admin()
    {
        var policy = Cluster().Admission.OfType<V1ValidatingAdmissionPolicy>().Single();

        policy.Spec.FailurePolicy.Should().Be("Fail");
        policy
            .Spec.MatchConstraints.ResourceRules.Single()
            .Resources.Should()
            .Equal("pods", "pods/ephemeralcontainers");
        policy
            .Spec.MatchConstraints.ResourceRules.Single()
            .Operations.Should()
            .Equal("CREATE", "UPDATE");
        var baseline = policy
            .Spec.Variables.Single(v => string.Equals(v.Name, "baseline", StringComparison.Ordinal))
            .Expression;
        baseline.Should().NotContain("NET_ADMIN").And.Contain("'CHOWN'").And.Contain("'SETUID'");

        var containers = policy.Spec.Validations[0].Expression;
        containers
            .Should()
            .StartWith("variables.containers.all(c, ")
            .And.Contain("a in variables.baseline");

        var inits = policy.Spec.Validations[1].Expression;
        inits
            .Should()
            .Contain("c.name == 'egress-rules'")
            .And.Contain("c.image in variables.egressImages")
            .And.Contain("!has(c.command)")
            .And.Contain("c.args == ['rules']")
            .And.Contain("!has(c.restartPolicy)")
            .And.Contain("== ['NET_ADMIN']");

        policy.Spec.Validations[2].Expression.Should().Contain("privileged");
        policy
            .Spec.Validations[3]
            .Expression.Should()
            .Contain("hostNetwork")
            .And.Contain("hostPID");
    }

    [Test]
    public void The_policy_names_the_egress_images_it_allows_this_one_first()
    {
        var images = (V1ValidatingAdmissionPolicy policy) =>
            policy
                .Spec.Variables.Single(v =>
                    string.Equals(v.Name, "egressImages", StringComparison.Ordinal)
                )
                .Expression;
        const string previous =
            "ghcr.io/traxsharp/trax-egress@sha256:fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

        images(Cluster().Admission.OfType<V1ValidatingAdmissionPolicy>().Single())
            .Should()
            .Be($"['{Image}']");
        images(
                KubernetesTarget
                    .EgressAdmission(Held(), "orders-staging", Image, [previous, Image])
                    .OfType<V1ValidatingAdmissionPolicy>()
                    .Single()
            )
            .Should()
            .Be($"['{Image}', '{previous}']");
    }

    [Test]
    public void The_policy_refuses_the_egress_uid_to_every_container_but_the_sidecar()
    {
        var policy = Cluster().Admission.OfType<V1ValidatingAdmissionPolicy>().Single();

        var uid = policy.Spec.Validations.Single(v =>
            v.Expression.Contains("runAsUser", StringComparison.Ordinal)
        );
        uid.Expression.Should()
            .Contain("object.spec.securityContext.runAsUser != 61000")
            .And.Contain("c.securityContext.runAsUser != 61000")
            .And.Contain("variables.containers.all(c, ")
            .And.Contain("variables.inits.all(c, ")
            .And.Contain("c.name == 'egress'")
            .And.Contain("c.image in variables.egressImages")
            .And.Contain("c.args == ['serve']")
            .And.Contain("c.restartPolicy == 'Always'");
        uid.Message.Should().Contain("61000");
        PodOf(Cluster(), "hub").InitContainers[1].Command.Should().BeNull();
    }

    [Test]
    public void The_policy_refuses_setuid_and_privilege_escalation_in_a_held_pod()
    {
        // A container that keeps SETUID could switch to the egress uid after admission, and one that may gain
        // privileges could exec a binary set-uid to it. In a pod with the egress rules, every container (init and
        // ephemeral ones included) drops both and sets allowPrivilegeEscalation false. Pods without the rules (the
        // in-cluster Postgres, whose entrypoint needs SETUID) are not held, so the uid gets them nothing.
        var policy = Cluster().Admission.OfType<V1ValidatingAdmissionPolicy>().Single();

        policy
            .Spec.Variables.Single(v => string.Equals(v.Name, "held", StringComparison.Ordinal))
            .Expression.Should()
            .Be("variables.inits.exists(c, c.name == 'egress-rules')");
        var setid = policy.Spec.Validations.Single(v =>
            v.Expression.Contains("SETUID", StringComparison.Ordinal)
        );
        setid
            .Expression.Should()
            .StartWith("!variables.held || (variables.containers + variables.inits).all(c, ")
            .And.Contain("c.securityContext.allowPrivilegeEscalation == false")
            .And.Contain("'ALL' in c.securityContext.capabilities.drop")
            .And.Contain("'SETUID' in c.securityContext.capabilities.drop")
            .And.Contain("'SETGID' in c.securityContext.capabilities.drop")
            .And.Contain("!('SETUID' in ")
            .And.Contain("!('SETGID' in ");
        setid.Message.Should().Contain("SETUID").And.Contain("allowPrivilegeEscalation");

        // Everything trax renders in a held pod already passes.
        var pod = PodOf(Cluster(), "hub");
        foreach (var c in pod.Containers.Concat(pod.InitContainers))
        {
            c.SecurityContext.AllowPrivilegeEscalation.Should().BeFalse(c.Name);
            c.SecurityContext.Capabilities.Drop.Should().Equal(["ALL"], c.Name);
            (c.SecurityContext.Capabilities.Add ?? [])
                .Should()
                .NotContain(["SETUID", "SETGID"], c.Name);
        }
    }

    [Test]
    public void Kubernetes_without_egress_renders_no_policy_and_no_init_containers()
    {
        var m = KubernetesTarget.Render(
            DeployKubernetesTests.File_(),
            "staging",
            DeployKubernetesTests.Exports(),
            Tag
        );

        m.Admission.Should().BeEmpty();
        m.Services.OfType<V1Deployment>()
            .Should()
            .OnlyContain(d => d.Spec.Template.Spec.InitContainers == null);
    }

    [Test]
    public void Kubernetes_gives_the_rules_and_the_sidecar_a_fixed_memory_limit()
    {
        var pod = PodOf(Cluster(), "hub");

        foreach (var c in pod.InitContainers)
        {
            c.Resources.Limits["memory"].ToString().Should().Be("128Mi", c.Name);
            c.Resources.Requests["memory"].ToString().Should().Be("128Mi", c.Name);
        }
        pod.Containers.Single()
            .Resources.Should()
            .BeNull("the service names no memory, and gets no default");
    }

    [Test]
    public void The_policy_is_applied_before_any_pod_and_written_into_the_plan()
    {
        var m = Cluster();

        m.All.Select(o => o.Kind)
            .Should()
            .StartWith([
                "Namespace",
                "ValidatingAdmissionPolicy",
                "ValidatingAdmissionPolicyBinding",
            ]);
        var yaml = KubernetesTarget.Yaml(m);
        yaml.Select(f => f.Path)
            .Should()
            .Contain("01-validatingadmissionpolicy-trax-egress-orders-staging.yaml");
        yaml.Single(f => f.Path.Contains("validatingadmissionpolicy-", StringComparison.Ordinal))
            .Content.Should()
            .Contain(Image);
    }

    // ---- podman ------------------------------------------------------------------------------------------

    private static PodmanStack Podman(DeployFile file) =>
        PodmanTarget.Render(
            file,
            "home",
            DeployKubernetesTests.Exports(),
            Tag,
            "/src/.trax/deploy/podman/orders-home",
            "/home/u/.config/trax/orders-home/secret.yaml"
        );

    private static DeployFile HeldOnPodman()
    {
        var file = DeployPodmanTests.File_();
        file.EgressImage = Image;
        file.Services["hub"].Egress = ["api.anthropic.com"];
        return file;
    }

    [Test]
    public void Podman_runs_the_sidecar_as_an_ordinary_container_that_puts_the_rules_back_as_it_starts()
    {
        var hub = Podman(HeldOnPodman())
            .Files.Single(f =>
                string.Equals(f.Path, "orders-home-hub.yaml", StringComparison.Ordinal)
            )
            .Content;

        // kube play waits for every init container to exit, so a native sidecar would hold the pod.
        hub.Should().NotContain("restartPolicy: Always");
        hub.Should().NotContain("startupProbe");
        var deployment = KubernetesYaml.Deserialize<V1Deployment>(
            hub[hub.IndexOf("apiVersion", StringComparison.Ordinal)..]
        );
        var pod = deployment.Spec.Template.Spec;
        pod.InitContainers.Select(c => c.Name).Should().Equal("egress-rules");
        pod.Containers.Select(c => c.Name).Should().Equal("hub", "egress");
        var sidecar = pod.Containers[1];
        sidecar.Args.Should().Equal("run");
        sidecar.SecurityContext.RunAsUser.Should().Be(0);
        sidecar.SecurityContext.Capabilities.Add.Should().Equal("NET_ADMIN", "SETGID", "SETUID");
        pod.Containers[0].SecurityContext.Capabilities.Add.Should().BeNull();
        sidecar.Resources.Limits["memory"].ToString().Should().Be("128Mi");
        pod.InitContainers[0].Resources.Limits["memory"].ToString().Should().Be("128Mi");
        deployment
            .Spec.Template.Metadata.Annotations[PodmanTarget.InitContainerTypeAnnotation]
            .Should()
            .Be("always", "the rules run again on every start of the pod, not only its first");
    }

    [Test]
    public void Podman_leaves_the_process_namespace_unshared()
    {
        // kube play under --userns=nomap cannot start a pod that shares its PID namespace (crun 1.31: "open `.`:
        // Permission denied"), so on podman the sidecar does not see the app's processes.
        var hub = Podman(HeldOnPodman())
            .Files.Single(f =>
                string.Equals(f.Path, "orders-home-hub.yaml", StringComparison.Ordinal)
            )
            .Content;
        var deployment = KubernetesYaml.Deserialize<V1Deployment>(
            hub[hub.IndexOf("apiVersion", StringComparison.Ordinal)..]
        );

        deployment.Spec.Template.Spec.ShareProcessNamespace.Should().BeNull();
    }

    [Test]
    public void Podman_writes_no_admission_policy()
    {
        Podman(HeldOnPodman())
            .Files.Should()
            .OnlyContain(f =>
                !f.Content.Contains("ValidatingAdmissionPolicy", StringComparison.Ordinal)
            );
    }

    [Test]
    public void Podman_stops_the_migrate_pods_sidecar_once_the_migration_has_run()
    {
        var unit = Podman(HeldOnPodman())
            .Files.Single(f =>
                string.Equals(f.Path, "orders-home-hub.kube", StringComparison.Ordinal)
            )
            .Content;

        unit.Should()
            .Contain(
                "ExecStartPre=-/usr/bin/podman pod stop --ignore -t 10 orders-home-migrate-pod\n"
            );
        Podman(DeployPodmanTests.File_())
            .Files.Single(f =>
                string.Equals(f.Path, "orders-home-hub.kube", StringComparison.Ordinal)
            )
            .Content.Should()
            .NotContain("pod stop");
    }

    [Test]
    public void Podman_leaves_a_pod_without_egress_as_it_was()
    {
        var worker = Podman(HeldOnPodman())
            .Files.Single(f =>
                string.Equals(f.Path, "orders-home-worker.yaml", StringComparison.Ordinal)
            )
            .Content;

        worker
            .Should()
            .NotContain("egress")
            .And.NotContain(PodmanTarget.InitContainerTypeAnnotation);
    }
}
