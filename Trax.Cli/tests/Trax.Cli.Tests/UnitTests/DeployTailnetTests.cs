using AwesomeAssertions;
using k8s;
using k8s.Models;
using Trax.Cli.Commands;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Aws;
using Trax.Cli.Deploy.Kubernetes;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>"exposure": "tailnet"</c> on a kubernetes environment. On a cluster: the Tailscale operator's Ingress in front of
/// a Caddy edge that sets the proxy secret, network policies that deny by default and admit only that path, and no
/// LoadBalancer, NodePort or other Ingress. Under podman: the edge and Tailscale as sidecars in the hub's pod and no
/// port published on the host. Every other target refuses the field, and a host that signs people in by tailnet
/// identity is refused anywhere but the exposed hub.
///
/// <para>Guard for <c>Trax.Docs/adr/0063-a-tailnet-identity-is-trusted-only-from-the-proxy-the-deploy-controls.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0063-a-tailnet-identity-is-trusted-only-from-the-proxy-the-deploy-controls.md"
)]
public class DeployTailnetTests
{
    private const string Tag = "20261124-120000-abcdef123456";

    private static DeployFile Tailnet(Action<DeployEnvironment>? change = null)
    {
        var file = DeployKubernetesTests.File_();
        var env = file.Environments["staging"];
        env.Exposure = DeployEnvironment.Tailnet;
        change?.Invoke(env);
        return file;
    }

    private static Dictionary<string, DeployManifest> Exports(
        bool rabbit = false,
        string? signsInByTailnet = "hub"
    )
    {
        var exports = DeployKubernetesTests.Exports(rabbit);
        if (signsInByTailnet is not null)
            exports[signsInByTailnet]
                .Authentication.Add(
                    new DeployAuthenticationScheme
                    {
                        Name = DeployExposure.TailscaleScheme,
                        Handler = TailnetSignIn.Handler,
                    }
                );
        return exports;
    }

    private static KubernetesManifests Render(
        DeployFile? file = null,
        Dictionary<string, DeployManifest>? exports = null
    ) => KubernetesTarget.Render(file ?? Tailnet(), "staging", exports ?? Exports(), Tag);

    private static T One<T>(KubernetesManifests m, string name)
        where T : IKubernetesObject<V1ObjectMeta> =>
        m
            .All.OfType<T>()
            .Single(o => string.Equals(o.Metadata.Name, name, StringComparison.Ordinal));

    private static List<V1EnvVar> Env(V1Deployment d) =>
        [.. d.Spec.Template.Spec.Containers[0].Env];

    // ---- What a cluster gets ----

    [Test]
    public void A_tailnet_exposure_renders_no_LoadBalancer_or_NodePort_and_only_the_operators_Ingress()
    {
        var m = Render();

        m.All.OfType<V1Service>()
            .Select(s => s.Spec.Type)
            .Should()
            .OnlyContain(t => string.Equals(t, "ClusterIP", StringComparison.Ordinal));
        m.All.OfType<V1Service>().Should().OnlyContain(s => s.Spec.ExternalIPs == null);
        var ingress = m.All.OfType<V1Ingress>().Should().ContainSingle().Which;
        ingress.Spec.IngressClassName.Should().Be(KubernetesTailnet.IngressClass);
        ingress.Spec.Rules.Should().BeNull();
        ingress.Spec.DefaultBackend.Service.Name.Should().Be(KubernetesTailnet.EdgeName);
        ingress.Spec.DefaultBackend.Service.Port.Number.Should().Be(KubernetesTailnet.EdgePort);
        ingress.Spec.Tls.Single().Hosts.Should().Equal("orders-staging");
        m.All.OfType<V1Deployment>()
            .SelectMany(d => d.Spec.Template.Spec.Containers)
            .SelectMany(c => c.Ports ?? [])
            .Should()
            .OnlyContain(p => p.HostPort == null && p.HostIP == null);
        m.All.OfType<V1Deployment>()
            .Should()
            .OnlyContain(d => d.Spec.Template.Spec.HostNetwork != true);
    }

    [Test]
    public void A_tailnet_exposure_renders_a_canary_under_the_edges_policy_and_a_probe_it_does_not_admit()
    {
        var m = Render();
        var canary = m.TailnetCanary.NotNull();
        var listener = canary.Listener.Spec.Template;
        var probe = canary.Probe.Spec.Template;
        var edgePolicy = m.Policies.Single(p =>
            string.Equals(p.Metadata.Name, "orders-tailnet", StringComparison.Ordinal)
        );

        foreach (var (key, value) in edgePolicy.Spec.PodSelector.MatchLabels)
            listener.Metadata.Labels.Should().Contain(key, value);
        probe.Metadata.Labels.Should().NotContainKey(KubernetesTailnet.RoleLabel);
        probe.Metadata.Labels[KubernetesTarget.AppLabel].Should().Be("orders");
        foreach (var (key, value) in canary.Service.Spec.Selector)
            listener.Metadata.Labels.Should().Contain(key, value);
        canary
            .Service.Spec.Selector[KubernetesTarget.NameLabel]
            .Should()
            .NotBe(KubernetesTailnet.EdgeName, "the canary must not take the edge's traffic");
        var control = canary.Control.Spec.Template;
        control.Metadata.Labels.Should().NotContainKey(KubernetesTailnet.RoleLabel);
        control.Metadata.Labels[KubernetesTarget.AppLabel].Should().Be("orders");
        foreach (var (key, value) in canary.ControlService.Spec.Selector)
            control.Metadata.Labels.Should().Contain(key, value);
        canary
            .ControlService.Spec.Selector[KubernetesTarget.NameLabel]
            .Should()
            .Be(KubernetesTailnet.ControlName);
        string.Join(' ', probe.Spec.Containers.Single().Command)
            .Should()
            .Contain($"{KubernetesTailnet.CanaryName}:{KubernetesTailnet.EdgePort}")
            .And.Contain($"{KubernetesTailnet.ControlName}:{KubernetesTailnet.EdgePort}");
        new[] { listener, control, probe }
            .SelectMany(t => t.Spec.Containers)
            .SelectMany(c => c.Env ?? [])
            .Should()
            .OnlyContain(v => v.ValueFrom == null, "neither holds a secret");
        m.All.Should().NotContain(o => o.Metadata.Name.Contains(KubernetesTailnet.CanaryName));
        m.All.Should().NotContain(o => o.Metadata.Name.Contains(KubernetesTailnet.ControlName));
    }

    [Test]
    public void The_policies_admit_the_probe_to_the_control_and_not_to_the_canary()
    {
        var m = Render();
        var canary = m.TailnetCanary.NotNull();
        var within = m.Policies.Single(p =>
            string.Equals(p.Metadata.Name, "orders-within", StringComparison.Ordinal)
        );
        bool Selects(V1LabelSelector selector, IDictionary<string, string> labels) =>
            selector.MatchLabels.All(l =>
                labels.TryGetValue(l.Key, out var v)
                && string.Equals(v, l.Value, StringComparison.Ordinal)
            )
            && (selector.MatchExpressions ?? [])
                .Where(e =>
                    string.Equals(e.OperatorProperty, "DoesNotExist", StringComparison.Ordinal)
                )
                .All(e => !labels.ContainsKey(e.Key));
        var probe = canary.Probe.Spec.Template.Metadata.Labels;

        Selects(within.Spec.PodSelector, canary.Control.Spec.Template.Metadata.Labels)
            .Should()
            .BeTrue();
        Selects(within.Spec.Ingress.Single().FromProperty.Single().PodSelector, probe)
            .Should()
            .BeTrue();
        Selects(within.Spec.PodSelector, canary.Listener.Spec.Template.Metadata.Labels)
            .Should()
            .BeFalse();
    }

    [Test]
    public void Without_an_exposure_there_is_no_canary()
    {
        var m = KubernetesTarget.Render(
            DeployKubernetesTests.File_(),
            "staging",
            DeployKubernetesTests.Exports(),
            Tag
        );

        m.TailnetCanary.Should().BeNull();
    }

    [Test]
    public void The_tailnet_hostname_is_the_environments_own_when_it_names_one()
    {
        var m = Render(
            Tailnet(e => e.TailnetOptions = new DeployEnvironmentTailnet { Hostname = "orders" })
        );

        m.All.OfType<V1Ingress>().Single().Spec.Tls.Single().Hosts.Should().Equal("orders");
    }

    [Test]
    public void Without_an_exposure_nothing_but_ClusterIP_Services_and_the_policies_are_rendered()
    {
        var m = KubernetesTarget.Render(
            DeployKubernetesTests.File_(),
            "staging",
            DeployKubernetesTests.Exports(),
            Tag
        );

        m.All.OfType<V1Ingress>().Should().BeEmpty();
        m.All.OfType<V1ConfigMap>().Should().BeEmpty();
        m.ProxySecretName.Should().BeNull();
        m.Policies.Select(p => p.Metadata.Name).Should().Equal("orders-deny", "orders-within");
        Env(One<V1Deployment>(m, "hub"))
            .Select(v => v.Name)
            .Should()
            .NotContain([DeployExposure.Variable, DeployExposure.ProxySecretVariable]);
    }

    [Test]
    public void The_edge_sets_the_proxy_secret_from_the_Secret_and_runs_unprivileged()
    {
        var m = Render();

        var config = One<V1ConfigMap>(m, KubernetesTailnet.EdgeName);
        var caddyfile = config.Data[KubernetesTarget.EdgeConfigFile];
        caddyfile.Should().Contain("admin off");
        caddyfile.Should().Contain("reverse_proxy hub:8080");
        caddyfile
            .Should()
            .Contain(
                $"header_up {DeployExposure.ProxySecretHeader} {{env.{KubernetesTailnet.EdgeSecretVariable}}}"
            );
        caddyfile.Should().NotContain("bind", "on a cluster the edge's own pod is its boundary");

        var edge = One<V1Deployment>(m, KubernetesTailnet.EdgeName);
        edge.Metadata.Labels[KubernetesTailnet.RoleLabel].Should().Be(KubernetesTailnet.EdgeRole);
        var pod = edge.Spec.Template;
        pod.Metadata.Labels[KubernetesTailnet.RoleLabel].Should().Be(KubernetesTailnet.EdgeRole);
        pod.Metadata.Annotations[KubernetesTailnet.GenerationAnnotation].Should().Be(Tag);
        pod.Spec.HostUsers.Should().BeFalse();
        pod.Spec.AutomountServiceAccountToken.Should().BeFalse();
        var caddy = pod.Spec.Containers.Single();
        caddy.Image.Should().Be(KubernetesTailnet.CaddyImage);
        caddy.SecurityContext.RunAsNonRoot.Should().BeTrue();
        caddy.SecurityContext.RunAsUser.Should().Be(KubernetesTailnet.EdgeUser);
        caddy.SecurityContext.AllowPrivilegeEscalation.Should().BeFalse();
        caddy.SecurityContext.ReadOnlyRootFilesystem.Should().BeTrue();
        caddy.SecurityContext.Capabilities.Drop.Should().Equal("ALL");
        caddy.SecurityContext.Capabilities.Add.Should().BeNull();
        var secret = caddy.Env.Single(v =>
            string.Equals(v.Name, KubernetesTailnet.EdgeSecretVariable, StringComparison.Ordinal)
        );
        secret.Value.Should().BeNull("no secret value is written into a manifest");
        secret.ValueFrom.SecretKeyRef.Name.Should().Be("orders-staging-tailnet");
        secret.ValueFrom.SecretKeyRef.Key.Should().Be(KubernetesTailnet.ProxySecretKey);
    }

    [Test]
    public void Only_the_hub_is_told_it_is_on_a_tailnet_and_given_the_proxy_secret()
    {
        var m = Render();

        var hub = One<V1Deployment>(m, "hub");
        var vars = Env(hub);
        vars.Single(v => string.Equals(v.Name, DeployExposure.Variable, StringComparison.Ordinal))
            .Value.Should()
            .Be(DeployExposure.Tailnet);
        var secret = vars.Single(v =>
            string.Equals(v.Name, DeployExposure.ProxySecretVariable, StringComparison.Ordinal)
        );
        secret.Value.Should().BeNull();
        secret.ValueFrom.SecretKeyRef.Name.Should().Be(m.ProxySecretName);
        hub.Spec.Template.Metadata.Annotations[KubernetesTailnet.GenerationAnnotation]
            .Should()
            .Be(Tag);
        foreach (
            var other in new[] { One<V1Deployment>(m, "worker"), One<V1Deployment>(m, "reports") }
        )
            Env(other)
                .Select(v => v.Name)
                .Should()
                .NotContain([DeployExposure.Variable, DeployExposure.ProxySecretVariable]);
        m.Migrate.Spec.Template.Spec.Containers[0]
            .Env.Select(v => v.Name)
            .Should()
            .NotContain([DeployExposure.Variable, DeployExposure.ProxySecretVariable]);
        m.ProxySecretName.Should().Be("orders-staging-tailnet");
    }

    // ---- The network policies ----

    [Test]
    public void Every_pod_in_the_namespace_refuses_ingress_by_default()
    {
        var deny = Render()
            .Policies.Single(p =>
                string.Equals(p.Metadata.Name, "orders-deny", StringComparison.Ordinal)
            );

        deny.Spec.PodSelector.MatchLabels.Should().BeNullOrEmpty();
        deny.Spec.PodSelector.MatchExpressions.Should().BeNullOrEmpty();
        deny.Spec.PolicyTypes.Should().Equal("Ingress");
        deny.Spec.Ingress.Should().BeEmpty();
    }

    [Test]
    public void The_apps_pods_but_the_edge_accept_only_the_apps_pods_but_the_edge()
    {
        var within = Render()
            .Policies.Single(p =>
                string.Equals(p.Metadata.Name, "orders-within", StringComparison.Ordinal)
            );

        AppButEdge(within.Spec.PodSelector);
        within.Spec.PolicyTypes.Should().Equal("Ingress");
        var from = within.Spec.Ingress.Single().FromProperty.Single();
        from.NamespaceSelector.Should().BeNull("the app's pods in this namespace only");
        AppButEdge(from.PodSelector);
    }

    private static void AppButEdge(V1LabelSelector selector)
    {
        selector
            .MatchLabels.Should()
            .Equal(
                new Dictionary<string, string>(StringComparer.Ordinal) { ["trax/app"] = "orders" }
            );
        var expression = selector.MatchExpressions.Single();
        expression.Key.Should().Be(KubernetesTailnet.RoleLabel);
        expression.OperatorProperty.Should().Be("DoesNotExist");
    }

    [Test]
    public void The_database_and_broker_are_covered_by_the_policies_as_app_pods()
    {
        var m = Render(exports: Exports(rabbit: true));

        foreach (var name in new[] { KubernetesTarget.Database, KubernetesTarget.Broker })
        {
            var labels = m
                .Backing.OfType<V1Service>()
                .Single(s => string.Equals(s.Metadata.Name, name, StringComparison.Ordinal))
                .Spec.Selector;
            labels["trax/app"].Should().Be("orders");
            var pod = m
                .Backing.Select(o =>
                    o switch
                    {
                        V1StatefulSet s => s.Spec.Template,
                        V1Deployment d => d.Spec.Template,
                        _ => null,
                    }
                )
                .OfType<V1PodTemplateSpec>()
                .Single(t =>
                    string.Equals(
                        t.Metadata.Labels[KubernetesTarget.NameLabel],
                        name,
                        StringComparison.Ordinal
                    )
                );
            pod.Metadata.Labels.Should().NotContainKey(KubernetesTailnet.RoleLabel);
        }
    }

    [Test]
    public void The_edge_accepts_only_the_operators_proxy_pod_for_its_Ingress()
    {
        var tailnet = Render()
            .Policies.Single(p =>
                string.Equals(p.Metadata.Name, "orders-tailnet", StringComparison.Ordinal)
            );

        tailnet
            .Spec.PodSelector.MatchLabels.Should()
            .Equal(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["trax/app"] = "orders",
                    [KubernetesTailnet.RoleLabel] = KubernetesTailnet.EdgeRole,
                }
            );
        var rule = tailnet.Spec.Ingress.Single();
        rule.Ports.Single().Port.Value.Should().Be("8081");
        var from = rule.FromProperty.Single();
        from.NamespaceSelector.MatchLabels.Should()
            .Equal(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["kubernetes.io/metadata.name"] = "tailscale",
                }
            );
        from.PodSelector.MatchLabels.Should()
            .Equal(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["tailscale.com/managed"] = "true",
                    ["tailscale.com/parent-resource-type"] = "ingress",
                    ["tailscale.com/parent-resource"] = KubernetesTailnet.EdgeName,
                    ["tailscale.com/parent-resource-ns"] = "orders-staging",
                }
            );
    }

    [Test]
    public void The_operators_namespace_is_the_environments_own_when_it_names_one()
    {
        var m = Render(
            Tailnet(e =>
                e.TailnetOptions = new DeployEnvironmentTailnet { OperatorNamespace = "ts-system" }
            )
        );

        m.Policies.Single(p =>
                string.Equals(p.Metadata.Name, "orders-tailnet", StringComparison.Ordinal)
            )
            .Spec.Ingress.Single()
            .FromProperty.Single()
            .NamespaceSelector.MatchLabels["kubernetes.io/metadata.name"]
            .Should()
            .Be("ts-system");
    }

    [Test]
    public void The_hub_accepts_the_edge_on_its_port()
    {
        var hub = Render()
            .Policies.Single(p =>
                string.Equals(p.Metadata.Name, "orders-tailnet-hub", StringComparison.Ordinal)
            );

        hub.Spec.PodSelector.MatchLabels[KubernetesTarget.NameLabel].Should().Be("hub");
        var rule = hub.Spec.Ingress.Single();
        rule.Ports.Single().Port.Value.Should().Be("8080");
        rule.FromProperty.Single()
            .PodSelector.MatchLabels[KubernetesTailnet.RoleLabel]
            .Should()
            .Be(KubernetesTailnet.EdgeRole);
    }

    [Test]
    public void The_policies_are_applied_before_anything_they_protect()
    {
        var all = Render().All.Select(o => o.Kind).ToList();

        all.IndexOf("NetworkPolicy").Should().Be(1);
        all.LastIndexOf("NetworkPolicy").Should().BeLessThan(all.IndexOf("StatefulSet"));
    }

    // ---- Where the field and the sign-in scheme are refused ----

    [TestCase("public", "*\"public\" is not offered yet*")]
    [TestCase("internet", "*exposure 'internet' is not one trax renders*")]
    public void Any_exposure_but_tailnet_is_refused(string exposure, string message)
    {
        var file = Tailnet(e => e.Exposure = exposure);

        var act = () => file.Environment("staging");

        act.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [Test]
    public void An_aws_environment_refuses_exposure()
    {
        var file = AwsStackTemplateTests.File_();
        file.Environments["production"].Exposure = DeployEnvironment.Tailnet;

        var act = () => file.Environment("production");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*exposure and tailnet are not fields this target reads*load balancer*");
    }

    [Test]
    public void A_compose_environment_refuses_exposure()
    {
        var file = DeployKubernetesTests.File_();
        file.Environments["home"] = new DeployEnvironment
        {
            Target = DeployEnvironment.Compose,
            Exposure = DeployEnvironment.Tailnet,
        };

        var act = () => file.Environment("home");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*not fields this target reads*");
    }

    [Test]
    public void Tailnet_options_without_a_tailnet_exposure_are_refused()
    {
        var file = Tailnet(e =>
        {
            e.Exposure = null;
            e.TailnetOptions = new DeployEnvironmentTailnet { Hostname = "orders" };
        });

        var act = () => file.Environment("staging");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*exposure is not \"tailnet\"*");
    }

    [TestCase("Orders")]
    [TestCase("orders.example")]
    [TestCase("-orders")]
    public void A_hostname_that_is_not_a_machine_name_is_refused(string hostname)
    {
        var file = Tailnet(e =>
            e.TailnetOptions = new DeployEnvironmentTailnet { Hostname = hostname }
        );

        var act = () => file.Environment("staging");

        act.Should().Throw<InvalidOperationException>().WithMessage("*not a machine name*");
    }

    [Test]
    public void A_service_named_like_the_edge_is_refused()
    {
        var file = Tailnet();
        file.Services[KubernetesTailnet.EdgeName] = file.Services["reports"];

        var act = () => file.Environment("staging");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*takes the name of the proxy*");
    }

    [TestCase(KubernetesTailnet.CanaryName)]
    [TestCase(KubernetesTailnet.ControlName)]
    public void A_service_named_like_the_canary_is_refused(string name)
    {
        var file = Tailnet();
        file.Services[name] = file.Services["reports"];

        var act = () => file.Environment("staging");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'{name}'*proves the edge's network policy*");
    }

    [TestCase("Trax__Deploy__Exposure", true)]
    [TestCase("Trax__Deploy__ProxySecret", true)]
    [TestCase("trax__deploy__exposure", false)]
    public async Task The_app_cannot_set_what_the_deploy_says_about_exposure(
        string name,
        bool asSetting
    )
    {
        var file = DeployKubernetesTests.File_();
        (asSetting ? file.Settings : file.Secrets).Add(name);
        var dir = Path.Combine(Path.GetTempPath(), $"trax-tailnet-{Guid.NewGuid():N}");
        foreach (var service in file.Services.Values)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(Path.Combine(dir, service.Project)) ?? dir
            );
            await File.WriteAllTextAsync(Path.Combine(dir, service.Project), "<Project />");
        }
        var path = Path.Combine(dir, DeployFile.FileName);
        await File.WriteAllTextAsync(path, file.Write());
        try
        {
            var act = () => DeployFile.ReadAsync(path);

            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*under Trax:Deploy, which trax deploy sets itself*");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void A_tailnet_exposure_with_two_hubs_is_refused()
    {
        var exports = Exports();
        exports["reports"] = exports["hub"];

        var act = () => Render(exports: exports);

        act.Should().Throw<InvalidOperationException>().WithMessage("*one hub*hub, reports*");
    }

    [Test]
    public void Tailnet_sign_in_without_a_tailnet_exposure_is_refused()
    {
        var act = () =>
            KubernetesTarget.Render(DeployKubernetesTests.File_(), "staging", Exports(), Tag);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*'hub' signs people in by their tailnet identity*not exposed on a tailnet*"
            );
    }

    [Test]
    public void Tailnet_sign_in_on_a_service_that_is_not_the_exposed_hub_is_refused()
    {
        var act = () => Render(exports: Exports(signsInByTailnet: "worker"));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*'worker'*only the hub 'hub'*");
    }

    [Test]
    public void A_renamed_tailnet_scheme_is_still_found_by_its_handler()
    {
        var exports = DeployKubernetesTests.Exports();
        exports["hub"]
            .Authentication.Add(
                new DeployAuthenticationScheme { Name = "Tailnet", Handler = TailnetSignIn.Handler }
            );

        var act = () =>
            KubernetesTarget.Render(DeployKubernetesTests.File_(), "staging", exports, Tag);

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void Tailnet_sign_in_is_refused_on_aws()
    {
        var exports = AwsStackTemplateTests.Exports();
        exports[exports.Keys.First()]
            .Authentication.Add(
                new DeployAuthenticationScheme
                {
                    Name = DeployExposure.TailscaleScheme,
                    Handler = TailnetSignIn.Handler,
                }
            );

        var act = () =>
            AwsStack.Build(
                AwsStackTemplateTests.File_(),
                "production",
                exports,
                cloudFrontPrefixList: null
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*public load balancer*");
    }

    [Test]
    public void Tailnet_sign_in_is_refused_on_compose()
    {
        var act = () => ComposeTarget.Render(DeployKubernetesTests.File_(), Exports());

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*publishes its hub on 127.0.0.1*");
    }

    [Test]
    public void The_export_runs_with_a_tailnet_exposure_and_a_placeholder_secret()
    {
        var values = DotnetHostExporter.Placeholders(DeployKubernetesTests.File_());

        values[DeployExposure.Variable].Should().Be(DeployExposure.Tailnet);
        values[DeployExposure.ProxySecretVariable].Length.Should().BeGreaterThanOrEqualTo(32);
    }

    // ---- The deploy ----

    [Test]
    public async Task A_deploy_writes_a_new_proxy_secret_after_the_migration_and_before_the_services()
    {
        var cluster = new KubernetesDeployerTests.FakeCluster
        {
            SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"],
        };
        var minted = new Queue<string>(["first-secret", "second-secret"]);
        var deployer = new KubernetesDeployer(
            cluster,
            new KubernetesDeployerTests.FakeImages(),
            _ => { },
            minted.Dequeue
        );

        await Deploy(deployer);
        var calls = cluster.Calls.ToList();
        await Deploy(deployer);

        var secrets = cluster.Applied.OfType<V1Secret>().ToList();
        secrets
            .Select(s => s.StringData[KubernetesTailnet.ProxySecretKey])
            .Should()
            .Equal("first-secret", "second-secret");
        secrets[0].Metadata.Name.Should().Be("orders-staging-tailnet");
        secrets[0].Metadata.Labels["trax/app"].Should().Be("orders");
        calls
            .IndexOf("apply Secret/orders-staging-tailnet")
            .Should()
            .BeGreaterThan(calls.FindIndex(c => c.StartsWith("wait-job", StringComparison.Ordinal)))
            .And.BeLessThan(calls.IndexOf("apply Deployment/hub"));
        calls
            .Should()
            .Contain([
                "apply Ingress/tailnet",
                "apply Deployment/tailnet",
                "wait Deployment/tailnet",
            ]);
        calls
            .IndexOf("apply NetworkPolicy/orders-deny")
            .Should()
            .BeLessThan(calls.IndexOf("apply Service/database"));
    }

    [Test]
    public async Task A_rollback_puts_back_the_proxy_secret_the_old_hub_was_started_with()
    {
        var cluster = RollingBackCluster();
        cluster.ProxySecrets["orders-staging-tailnet"] = "old-secret";
        var deployer = new KubernetesDeployer(
            cluster,
            new KubernetesDeployerTests.FakeImages(),
            _ => { },
            () => "new-secret"
        );

        var act = () => Deploy(deployer);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*rolled back*");
        cluster
            .Applied.OfType<V1Secret>()
            .Select(s => s.StringData[KubernetesTailnet.ProxySecretKey])
            .Should()
            .Equal("new-secret", "old-secret");
        var calls = cluster.Calls;
        var restored = calls.LastIndexOf("apply Secret/orders-staging-tailnet");
        restored
            .Should()
            .BeGreaterThan(calls.IndexOf("wait Deployment/hub"))
            .And.BeLessThan(calls.LastIndexOf("apply Deployment/tailnet"))
            .And.BeLessThan(calls.LastIndexOf("apply Deployment/hub"));
    }

    [Test]
    public async Task A_rollback_with_no_earlier_proxy_secret_leaves_the_new_one()
    {
        var cluster = RollingBackCluster();
        var deployer = new KubernetesDeployer(
            cluster,
            new KubernetesDeployerTests.FakeImages(),
            _ => { },
            () => "new-secret"
        );

        var act = () => Deploy(deployer);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*rolled back*");
        cluster
            .Applied.OfType<V1Secret>()
            .Select(s => s.StringData[KubernetesTailnet.ProxySecretKey])
            .Should()
            .Equal("new-secret");
    }

    [Test]
    public async Task A_refused_drain_leaves_the_proxy_secret_the_running_hub_was_started_with()
    {
        var cluster = RollingBackCluster();
        cluster.FailRollout = null;
        cluster.ProxySecrets["orders-staging-tailnet"] = "old-secret";
        cluster.JobResults["-drain-"] = new KubernetesJobResult(
            Succeeded: false,
            Reason: "BackoffLimitExceeded",
            "still busy"
        );
        var deployer = new KubernetesDeployer(
            cluster,
            new KubernetesDeployerTests.FakeImages(),
            _ => { },
            () => "new-secret"
        );

        var act = () => Deploy(deployer);

        await act.Should().ThrowAsync<InvalidOperationException>();
        cluster.Applied.OfType<V1Secret>().Should().BeEmpty();
        cluster.Calls.Should().NotContain("apply Deployment/hub");
    }

    /// <summary>A cluster running the hub and the edge from an earlier deploy, where the new hub never rolls out.</summary>
    private static KubernetesDeployerTests.FakeCluster RollingBackCluster()
    {
        var cluster = new KubernetesDeployerTests.FakeCluster
        {
            SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"],
            FailRollout = "Deployment/hub",
        };
        cluster.SecretLabels["orders-staging-tailnet"] = new(StringComparer.Ordinal)
        {
            ["trax/app"] = "orders",
        };
        cluster.Deployments["hub"] = KubernetesDeployerTests.Running("hub", "orders-hub:old");
        cluster.Deployments[KubernetesTailnet.EdgeName] = KubernetesDeployerTests.Running(
            KubernetesTailnet.EdgeName,
            KubernetesTailnet.CaddyImage
        );
        return cluster;
    }

    [Test]
    public async Task A_tailnet_deploy_proves_the_edges_policy_is_enforced_before_the_backing_comes_up()
    {
        var cluster = new KubernetesDeployerTests.FakeCluster
        {
            SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"],
        };
        var deployer = new KubernetesDeployer(
            cluster,
            new KubernetesDeployerTests.FakeImages(),
            _ => { }
        );

        await Deploy(deployer);

        var calls = cluster.Calls;
        var probe = calls.Single(c =>
            c.StartsWith("create-job", StringComparison.Ordinal)
            && c.Contains("-tailnet-probe-", StringComparison.Ordinal)
        );
        calls
            .IndexOf($"apply Deployment/{KubernetesTailnet.CanaryName}")
            .Should()
            .BeGreaterThan(calls.IndexOf("apply NetworkPolicy/orders-tailnet"));
        calls
            .IndexOf(probe)
            .Should()
            .BeGreaterThan(calls.IndexOf($"wait Deployment/{KubernetesTailnet.CanaryName}"))
            .And.BeLessThan(calls.IndexOf("apply Service/database"));
        calls
            .IndexOf($"delete Deployment/{KubernetesTailnet.CanaryName}")
            .Should()
            .BeGreaterThan(calls.IndexOf(probe))
            .And.BeLessThan(calls.IndexOf("apply Service/database"));
        calls.Should().Contain($"delete Service/{KubernetesTailnet.CanaryName}");
        calls
            .IndexOf(probe)
            .Should()
            .BeGreaterThan(calls.IndexOf($"wait Deployment/{KubernetesTailnet.ControlName}"));
        calls
            .Should()
            .Contain([
                $"apply Service/{KubernetesTailnet.ControlName}",
                $"delete Deployment/{KubernetesTailnet.ControlName}",
                $"delete Service/{KubernetesTailnet.ControlName}",
            ]);
    }

    [Test]
    public async Task A_cluster_that_lets_the_probe_reach_the_canary_is_refused_before_anything_runs()
    {
        var cluster = new KubernetesDeployerTests.FakeCluster
        {
            SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"],
        };
        cluster.JobResults["-tailnet-probe-"] = new KubernetesJobResult(
            Succeeded: false,
            Reason: "BackoffLimitExceeded",
            "reached the canary"
        );
        var deployer = new KubernetesDeployer(
            cluster,
            new KubernetesDeployerTests.FakeImages(),
            _ => { }
        );

        var act = () => Deploy(deployer);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not enforce NetworkPolicy*reached the canary*");
        cluster.Calls.Should().NotContain("apply Service/database");
        cluster.Applied.OfType<V1Secret>().Should().BeEmpty();
        cluster
            .Calls.Should()
            .Contain([
                $"delete Deployment/{KubernetesTailnet.CanaryName}",
                $"delete Service/{KubernetesTailnet.CanaryName}",
            ]);
    }

    [Test]
    public void Two_minted_secrets_differ_and_are_long_enough()
    {
        var a = KubernetesTailnet.MintSecret();
        var b = KubernetesTailnet.MintSecret();

        a.Should().NotBe(b);
        a.Length.Should().Be(43);
        a.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Test]
    public async Task A_proxy_secret_trax_did_not_write_is_refused()
    {
        var cluster = new KubernetesDeployerTests.FakeCluster
        {
            SecretKeys = ["POSTGRES_PASSWORD", "Stripe__ApiKey"],
        };
        cluster.SecretLabels["orders-staging-tailnet"] = [];
        var deployer = new KubernetesDeployer(
            cluster,
            new KubernetesDeployerTests.FakeImages(),
            _ => { }
        );

        var act = () => Deploy(deployer);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*trax deploy did not create it*");
        cluster.Applied.Should().BeEmpty("the ownership check runs before anything is applied");
    }

    [Test]
    public async Task Destroy_deletes_the_exposure_the_policies_and_the_proxy_secret()
    {
        var cluster = new KubernetesDeployerTests.FakeCluster
        {
            Namespace = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KubernetesTarget.AppLabel] = "orders",
            },
        };
        cluster.Existing.AddRange([
            new KubernetesObjectState("Ingress", "tailnet", "orders"),
            new KubernetesObjectState("ConfigMap", "tailnet", "orders"),
            new KubernetesObjectState("NetworkPolicy", "orders-deny", "orders"),
            new KubernetesObjectState("NetworkPolicy", "someone-elses", App: null),
        ]);
        cluster.SecretLabels["orders-staging-tailnet"] = new(StringComparer.Ordinal)
        {
            ["trax/app"] = "orders",
        };
        var deployer = new KubernetesDeployer(
            cluster,
            new KubernetesDeployerTests.FakeImages(),
            _ => { }
        );

        await deployer.DestroyAsync(
            Tailnet(),
            "staging",
            deleteData: false,
            CancellationToken.None
        );

        cluster
            .Calls.Where(c => c.StartsWith("delete", StringComparison.Ordinal))
            .Should()
            .Equal(
                "delete Ingress/tailnet",
                "delete ConfigMap/tailnet",
                "delete NetworkPolicy/orders-deny",
                "delete Secret/orders-staging-tailnet"
            );
    }

    private static Task Deploy(KubernetesDeployer deployer) =>
        deployer.DeployAsync(
            Tailnet(),
            "/src/trax.deploy.json",
            "staging",
            Exports(),
            Tag,
            CancellationToken.None
        );

    // ---- One host ----

    private const string SecretPath = "/home/u/.config/trax/orders-home/secret.yaml";

    private static DeployFile PodmanTailnet()
    {
        var file = DeployPodmanTests.File_();
        file.Environments["home"].Exposure = DeployEnvironment.Tailnet;
        return file;
    }

    private static PodmanStack Podman(
        DeployFile? file = null,
        Dictionary<string, DeployManifest>? exports = null
    ) =>
        PodmanTarget.Render(
            file ?? PodmanTailnet(),
            "home",
            exports ?? Exports(),
            Tag,
            "/src/.trax/deploy/podman/orders-home",
            SecretPath
        );

    private static Dictionary<string, string> Files(PodmanStack stack) =>
        stack.Files.ToDictionary(f => f.Path, f => f.Content, StringComparer.Ordinal);

    [Test]
    public void Under_podman_a_tailnet_hub_publishes_no_host_port()
    {
        var files = Files(Podman());

        files.Values.Should().NotContain(f => f.Contains("PublishPort", StringComparison.Ordinal));
        files["orders-home-hub.yaml"].Should().NotContain("hostPort").And.NotContain("hostNetwork");
    }

    [Test]
    public void Under_podman_without_an_exposure_the_hub_is_published_on_loopback_only()
    {
        var files = Files(Podman(DeployPodmanTests.File_(), Exports(signsInByTailnet: null)));

        files["orders-home-hub.kube"].Should().Contain("PublishPort=127.0.0.1:5080:8080");
    }

    [Test]
    public void Under_podman_tailnet_sign_in_without_a_tailnet_exposure_is_refused()
    {
        var act = () => Podman(DeployPodmanTests.File_());

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*'hub' signs people in by their tailnet identity*not exposed on a tailnet*"
            );
    }

    [Test]
    public void Under_podman_tailscale_and_the_edge_run_in_a_pod_of_their_own_beside_the_hub()
    {
        var stack = Podman();
        var files = Files(stack);
        var hub = files["orders-home-hub.yaml"];
        var tailnet = files["orders-home-tailnet.yaml"];

        hub.Should().NotContain(KubernetesTailnet.CaddyImage);
        hub.Should().NotContain(KubernetesTailnet.TailscaleImage);
        tailnet.Should().Contain("kind: ConfigMap").And.Contain("kind: PersistentVolumeClaim");
        tailnet.Should().Contain($"image: {KubernetesTailnet.CaddyImage}");
        tailnet.Should().Contain($"image: {KubernetesTailnet.TailscaleImage}");
        tailnet.Should().Contain("TS_USERSPACE").And.Contain("TS_AUTHKEY");
        tailnet
            .Should()
            .Contain($"container.seccomp.security.alpha.kubernetes.io/{KubernetesTailnet.EdgeName}")
            .And.Contain("container.seccomp.security.alpha.kubernetes.io/tailscale");
        tailnet.Should().NotContain("NET_ADMIN").And.NotContain("/dev/net/tun");
        stack.Units.Should().ContainInOrder("orders-home-hub", "orders-home-tailnet");
        stack.ServiceUnits.Should().NotContain("orders-home-tailnet");
        stack.SecretKeys.Should().Contain(KubernetesTailnet.AuthKeySecretKey);
        stack.ProxySecretName.Should().Be("orders-home-tailnet");
    }

    [Test]
    public void Under_podman_nothing_in_tailscales_network_namespace_listens_on_tcp()
    {
        // In userspace networking tailscaled hands a tailnet connection that serve does not take to 127.0.0.1 on the
        // same port. So the pod Tailscale runs in has no TCP listener at all: the edge listens on a Unix socket only
        // serve dials, and the hub is in another pod, reached over the app's network.
        var tailnet = Files(Podman())["orders-home-tailnet.yaml"];
        var config = KubernetesYaml.LoadAllFromString(tailnet).OfType<V1ConfigMap>().Single();
        var caddyfile = config.Data[KubernetesTarget.EdgeConfigFile];
        var serve = config.Data[KubernetesTarget.TailscaleServeFile];

        caddyfile
            .Should()
            .Contain($"bind unix/{KubernetesTailnet.EdgeSocket}|0222")
            .And.Contain("reverse_proxy hub:8080");
        caddyfile.Should().NotContain("127.0.0.1");
        serve.Should().Contain($"\"Proxy\":\"unix:{KubernetesTailnet.EdgeSocket}\"");
        serve.Should().NotContain("http://");
        var pod = KubernetesYaml
            .LoadAllFromString(tailnet)
            .OfType<V1Deployment>()
            .Single()
            .Spec.Template.Spec;
        pod.Containers.Select(c => c.Name)
            .Should()
            .BeEquivalentTo([KubernetesTailnet.EdgeName, "tailscale"]);
        pod.Containers.Should().OnlyContain(c => c.Ports == null || c.Ports.Count == 0);
        pod.Containers.Should()
            .OnlyContain(c =>
                c.VolumeMounts.Any(m =>
                    string.Equals(
                        m.MountPath,
                        KubernetesTailnet.EdgeSocketDirectory,
                        StringComparison.Ordinal
                    )
                )
            );
    }

    [Test]
    public void Under_podman_tailscale_runs_as_a_user_of_its_own()
    {
        var pod = KubernetesYaml
            .LoadAllFromString(Files(Podman())["orders-home-tailnet.yaml"])
            .OfType<V1Deployment>()
            .Single()
            .Spec.Template.Spec;
        var tailscale = pod.Containers.Single(c =>
            string.Equals(c.Name, "tailscale", StringComparison.Ordinal)
        );

        tailscale.SecurityContext.RunAsUser.Should().Be(KubernetesTailnet.TailscaleUser);
        tailscale.SecurityContext.RunAsNonRoot.Should().BeTrue();
        KubernetesTailnet
            .TailscaleUser.Should()
            .NotBe(DeployEgress.Uid)
            .And.NotBe(KubernetesTailnet.EdgeUser);
    }

    [Test]
    public void Under_podman_the_hub_and_the_tailnet_units_play_the_proxy_secret_and_no_other()
    {
        var files = Files(Podman());
        var proxySecret = PodmanTarget.ProxySecretPath(SecretPath);
        string[] readers = ["orders-home-hub.kube", "orders-home-tailnet.kube"];

        foreach (var reader in readers)
            files[reader].Should().Contain($"kube play --replace {proxySecret}");
        files
            .Where(f =>
                f.Key.EndsWith(".kube", StringComparison.Ordinal)
                && !readers.Contains(f.Key, StringComparer.Ordinal)
            )
            .Should()
            .OnlyContain(f => !f.Value.Contains(proxySecret, StringComparison.Ordinal));
        proxySecret.Should().Be("/home/u/.config/trax/orders-home/tailnet.yaml");
    }

    [Test]
    public void Under_podman_the_tailnet_unit_restarts_with_the_hub_so_both_read_one_proxy_secret()
    {
        var unit = Files(Podman())["orders-home-tailnet.kube"];

        // Requires= stops and restarts this unit with the hub's, and every deploy restarts the hub on its new image.
        unit.Should()
            .Contain("Requires=orders-home-hub.service")
            .And.Contain("After=orders-home-hub.service");
        unit.Should().NotContain("PublishPort");
    }

    [Test]
    public void Under_podman_the_new_proxy_secret_goes_live_only_once_the_drain_exits_0()
    {
        var before = Files(Podman());
        var stack = PodmanTarget.Render(
            PodmanTailnet(),
            "home",
            Exports(),
            "20261125-120000-bbbbbbbbbbbb",
            "/src/.trax/deploy/podman/orders-home",
            SecretPath
        );
        var release = stack.ReleaseFrom(before, secretsChanged: false);

        release.Drain.Should().BeTrue();
        DeployKubernetesCommands.RotatesProxySecret(stack, release).Should().BeTrue();
        var hint = DeployKubernetesCommands.InstallHint(
            stack,
            "/src/.trax/deploy/podman/orders-home",
            release
        );
        hint.Should()
            .Contain(
                "test \"$(/usr/bin/podman wait orders-home-drain-pod-drain)\" = 0 && "
                    + "mv -f /home/u/.config/trax/orders-home/tailnet.next.yaml "
                    + "/home/u/.config/trax/orders-home/tailnet.yaml && systemctl --user restart "
            );
        hint.Split('\n')
            .Where(l => l.Contains("tailnet.next.yaml", StringComparison.Ordinal))
            .Should()
            .ContainSingle("the move is part of the restart the drain guards and nowhere else");
    }

    [Test]
    public void Under_podman_a_first_release_moves_the_proxy_secret_in_before_it_starts_the_units()
    {
        var stack = Podman();
        var release = stack.ReleaseFrom(
            new Dictionary<string, string>(StringComparer.Ordinal),
            secretsChanged: true
        );

        DeployKubernetesCommands
            .InstallHint(stack, "/src/.trax/deploy/podman/orders-home", release)
            .Should()
            .Contain(
                "  mv -f /home/u/.config/trax/orders-home/tailnet.next.yaml "
                    + "/home/u/.config/trax/orders-home/tailnet.yaml && systemctl --user restart "
            );
    }

    [Test]
    public void Under_podman_a_release_that_restarts_neither_reader_writes_no_proxy_secret()
    {
        var stack = Podman();
        var release = stack.ReleaseFrom(Files(stack), secretsChanged: false);

        release.Restart.Should().BeEmpty();
        DeployKubernetesCommands.RotatesProxySecret(stack, release).Should().BeFalse();
        DeployKubernetesCommands
            .InstallHint(stack, "/src/.trax/deploy/podman/orders-home", release)
            .Should()
            .NotContain("tailnet");
    }

    [Test]
    public void The_proxy_secret_file_holds_the_minted_value_and_the_unit_files_do_not()
    {
        var stack = Podman();

        var yaml = PodmanTarget.ProxySecretYaml(stack, "minted-value");

        yaml.Should()
            .Contain("name: orders-home-tailnet")
            .And.Contain("proxy-secret: minted-value");
        Files(stack)
            .Values.Should()
            .NotContain(f => f.Contains("minted-value", StringComparison.Ordinal));
    }

    [Test]
    public void Under_podman_the_operators_namespace_is_refused()
    {
        var file = PodmanTailnet();
        file.Environments["home"].TailnetOptions = new DeployEnvironmentTailnet
        {
            OperatorNamespace = "tailscale",
        };

        var act = () => file.Environment("home");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*operatorNamespace is for a cluster*");
    }

    [Test]
    public void Under_podman_the_auth_key_is_not_also_an_app_secret()
    {
        var file = PodmanTailnet();
        file.Secrets.Add(KubernetesTailnet.AuthKeySecretKey);

        var act = () => file.Environment("home");

        act.Should().Throw<InvalidOperationException>().WithMessage("*never gives the app*");
    }
}
