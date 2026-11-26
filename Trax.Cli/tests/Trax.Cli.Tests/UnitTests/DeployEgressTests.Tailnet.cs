using AwesomeAssertions;
using k8s;
using k8s.Models;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Kubernetes;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

public partial class DeployEgressTests
{
    /// <summary>The exports with the hub signing people in by tailnet identity, as a tailnet exposure requires.</summary>
    private static Dictionary<string, DeployManifest> TailnetExports()
    {
        var exports = DeployKubernetesTests.Exports();
        exports["hub"]
            .Authentication.Add(
                new DeployAuthenticationScheme
                {
                    Name = DeployExposure.TailscaleScheme,
                    Handler = TailnetSignIn.Handler,
                }
            );
        return exports;
    }

    private static KubernetesManifests HeldOnTailnet()
    {
        var file = Held();
        file.Environments["staging"].Exposure = DeployEnvironment.Tailnet;
        return KubernetesTarget.Render(file, "staging", TailnetExports(), Tag);
    }

    [Test]
    public void A_held_hub_keeps_the_label_the_database_policy_admits_the_apps_pods_by()
    {
        var m = HeldOnTailnet();
        var within = m.Policies.Single(p =>
            p.Metadata.Name.EndsWith("-within", StringComparison.Ordinal)
        );
        var hubLabels = m
            .Services.OfType<V1Deployment>()
            .Single(d => string.Equals(d.Metadata.Name, "hub", StringComparison.Ordinal))
            .Spec.Template.Metadata.Labels;

        // The proxy connects from the hub pod's own address, so the database admits a held hub as the app's pod.
        foreach (var (key, value) in within.Spec.PodSelector.MatchLabels)
            hubLabels.Should().Contain(key, value);
        hubLabels.Should().NotContainKey(KubernetesTailnet.RoleLabel);
        within
            .Spec.Ingress.Single()
            .FromProperty.Single()
            .PodSelector.MatchLabels.Should()
            .Equal(within.Spec.PodSelector.MatchLabels);
    }

    [Test]
    public void The_network_policies_and_the_admission_policy_are_both_applied_before_any_pod()
    {
        var kinds = HeldOnTailnet().All.Select(o => o.Kind).ToList();

        kinds
            .Should()
            .StartWith([
                "Namespace",
                "ValidatingAdmissionPolicy",
                "ValidatingAdmissionPolicyBinding",
                "NetworkPolicy",
            ]);
        kinds.IndexOf("StatefulSet").Should().BeGreaterThan(kinds.LastIndexOf("NetworkPolicy"));
    }

    [Test]
    public void The_tailnet_edge_is_never_held_and_the_hub_still_is()
    {
        var m = HeldOnTailnet();
        var deployments = m
            .Services.OfType<V1Deployment>()
            .ToDictionary(d => d.Metadata.Name, StringComparer.Ordinal);

        deployments[KubernetesTailnet.EdgeName].Spec.Template.Spec.InitContainers.Should().BeNull();
        deployments[KubernetesTailnet.EdgeName]
            .Spec.Template.Spec.Containers.Should()
            .OnlyContain(c => c.SecurityContext.Capabilities.Add == null);
        deployments["hub"]
            .Spec.Template.Spec.InitContainers.Select(c => c.Name)
            .Should()
            .Equal("egress-rules", "egress");
    }

    [Test]
    public void The_egress_containers_work_under_a_read_only_root_and_the_pods_user_namespace()
    {
        var hub = HeldOnTailnet()
            .Services.OfType<V1Deployment>()
            .Single(d => string.Equals(d.Metadata.Name, "hub", StringComparison.Ordinal))
            .Spec.Template.Spec;

        hub.HostUsers.Should().BeFalse("the egress does not ask for the host's user namespace");
        foreach (var c in hub.InitContainers)
        {
            c.SecurityContext.ReadOnlyRootFilesystem.Should().BeTrue(c.Name);
            c.SecurityContext.AllowPrivilegeEscalation.Should().BeFalse(c.Name);
        }
        hub.Volumes.Select(v => v.Name).Should().Contain(KubernetesTarget.ScratchVolume);
    }

    // ---- One host, held and exposed ----

    private static (V1PodSpec Hub, V1PodSpec Tailnet) PodmanHeldTailnetHub(bool held)
    {
        var file = DeployPodmanTests.File_();
        file.Environments["home"].Exposure = DeployEnvironment.Tailnet;
        file.EgressImage = Image;
        if (held)
            file.Services["hub"].Egress = ["api.anthropic.com"];
        var files = PodmanTarget
            .Render(
                file,
                "home",
                TailnetExports(),
                Tag,
                "/src/.trax/deploy/podman/orders-home",
                "/home/u/.config/trax/orders-home/secret.yaml"
            )
            .Files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);
        static V1PodSpec Pod(string yaml) =>
            KubernetesYaml
                .LoadAllFromString(yaml)
                .OfType<V1Deployment>()
                .Single()
                .Spec.Template.Spec;
        return (Pod(files["orders-home-hub.yaml"]), Pod(files["orders-home-tailnet.yaml"]));
    }

    [Test]
    public void A_held_and_exposed_podman_hub_keeps_tailscale_out_of_its_pod_and_its_rules()
    {
        var (hub, tailnet) = PodmanHeldTailnetHub(held: true);

        hub.InitContainers.Select(c => c.Name).Should().Equal("egress-rules");
        hub.Containers.Select(c => c.Name).Should().BeEquivalentTo(["hub", "egress"]);
        tailnet.InitContainers.Should().BeNull();
        var tailscale = tailnet.Containers.Single(c =>
            string.Equals(c.Name, "tailscale", StringComparison.Ordinal)
        );
        tailscale
            .SecurityContext.RunAsUser.Should()
            .Be(KubernetesTailnet.TailscaleUser)
            .And.NotBe(DeployEgress.Uid);
        tailscale.SecurityContext.Capabilities.Add.Should().BeNull();
        hub.Containers.Single(c => string.Equals(c.Name, "hub", StringComparison.Ordinal))
            .SecurityContext.RunAsUser.Should()
            .NotBe(DeployEgress.Uid);
    }

    [Test]
    public void An_exposed_podman_hub_that_is_not_held_has_the_same_tailnet_pod()
    {
        var (hub, tailnet) = PodmanHeldTailnetHub(held: false);

        hub.InitContainers.Should().BeNull();
        hub.Containers.Select(c => c.Name).Should().Equal("hub");
        tailnet
            .Containers.Single(c => string.Equals(c.Name, "tailscale", StringComparison.Ordinal))
            .SecurityContext.RunAsUser.Should()
            .Be(KubernetesTailnet.TailscaleUser);
    }
}
