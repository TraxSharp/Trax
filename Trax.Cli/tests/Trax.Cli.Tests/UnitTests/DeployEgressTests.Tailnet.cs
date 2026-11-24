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

    private static V1PodSpec PodmanHeldTailnetHub(bool held)
    {
        var file = DeployPodmanTests.File_();
        file.Environments["home"].Exposure = DeployEnvironment.Tailnet;
        file.EgressImage = Image;
        if (held)
            file.Services["hub"].Egress = ["api.anthropic.com"];
        var hub = PodmanTarget
            .Render(
                file,
                "home",
                TailnetExports(),
                Tag,
                "/src/.trax/deploy/podman/orders-home",
                "/home/u/.config/trax/orders-home/secret.yaml"
            )
            .Files.Single(f =>
                string.Equals(f.Path, "orders-home-hub.yaml", StringComparison.Ordinal)
            )
            .Content;
        return KubernetesYaml
            .LoadAllFromString(hub)
            .OfType<V1Deployment>()
            .Single()
            .Spec.Template.Spec;
    }

    [Test]
    public void A_held_and_exposed_podman_hub_has_both_sidecars_and_tailscale_runs_as_the_egress_uid()
    {
        var pod = PodmanHeldTailnetHub(held: true);

        pod.InitContainers.Select(c => c.Name).Should().Equal("egress-rules");
        pod.Containers.Select(c => c.Name)
            .Should()
            .BeEquivalentTo(["hub", "egress", KubernetesTailnet.EdgeName, "tailscale"]);
        var tailscale = pod.Containers.Single(c =>
            string.Equals(c.Name, "tailscale", StringComparison.Ordinal)
        );
        tailscale
            .SecurityContext.RunAsUser.Should()
            .Be(
                DeployEgress.Uid,
                "the rules let only the egress uid out, and Tailscale must reach its control plane"
            );
        tailscale.SecurityContext.Capabilities.Add.Should().BeNull();
        pod.Containers.Single(c => string.Equals(c.Name, "hub", StringComparison.Ordinal))
            .SecurityContext.RunAsUser.Should()
            .NotBe(DeployEgress.Uid);
    }

    [Test]
    public void An_exposed_podman_hub_that_is_not_held_keeps_tailscale_as_it_was()
    {
        var pod = PodmanHeldTailnetHub(held: false);

        pod.InitContainers.Should().BeNull();
        pod.Containers.Select(c => c.Name).Should().NotContain("egress");
        pod.Containers.Single(c => string.Equals(c.Name, "tailscale", StringComparison.Ordinal))
            .SecurityContext.RunAsUser.Should()
            .BeNull();
    }
}
