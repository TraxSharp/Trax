using AwesomeAssertions;
using Trax.Cli.Deploy;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Tests.UnitTests;

public partial class DeployEgressTests
{
    private static string Compose(
        DeployFile? file = null,
        bool rabbit = true,
        string engine = DeployEnvironment.Podman
    ) =>
        ComposeTarget
            .Render(file ?? Held(), DeployKubernetesTests.Exports(rabbit), engine)[0]
            .Content;

    /// <summary>The lines of one service's block in a compose file, up to the next service or the volumes.</summary>
    private static string Block(string compose, string service)
    {
        var start = compose.IndexOf($"\n  {service}:\n", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, $"the compose file has a service {service}");
        var rest = compose[(start + 1)..];
        var next = rest.IndexOf("\n\n", StringComparison.Ordinal);
        return next < 0 ? rest : rest[..next];
    }

    [Test]
    public void Compose_puts_a_sidecar_beside_a_held_service_that_applies_the_rules_and_gives_up_root()
    {
        var sidecar = Block(Compose(), "hub-egress");

        sidecar.Should().Contain($"image: {Image}");
        sidecar.Should().Contain("user: \"0:0\"").And.Contain("command: [\"run\"]");
        sidecar
            .Should()
            .Contain(
                "TRAX_EGRESS_ALLOW: \"api.anthropic.com:443 registry.example.com:8443 tcp:database:5432 "
                    + "tcp:rabbitmq:5672\""
            );
        sidecar
            .Should()
            .Contain("cap_drop:\n      - ALL")
            .And.Contain("cap_add:\n      - NET_ADMIN\n      - SETGID\n      - SETUID")
            .And.Contain("no-new-privileges:true")
            .And.Contain("read_only: true")
            .And.Contain("userns_mode: nomap");
        sidecar.Should().Contain("test: [\"CMD\", \"/usr/local/bin/trax-egress\", \"ready\"]");
    }

    [Test]
    public void Compose_gives_the_sidecar_a_fixed_memory_limit_whatever_the_service_names()
    {
        var sidecar = Block(Compose(), "hub-egress");

        sidecar
            .Should()
            .Contain($"mem_limit: {DeployEgress.MemoryMiB}m")
            .And.Contain($"memswap_limit: {DeployEgress.MemoryMiB}m");
        DeployEgress.MemoryMiB.Should().Be(128);
    }

    [Test]
    public void Compose_joins_the_held_service_to_its_sidecar_and_publishes_its_port_there()
    {
        var compose = Compose();
        var hub = Block(compose, "hub");
        var sidecar = Block(compose, "hub-egress");

        hub.Should().Contain("network_mode: \"service:hub-egress\"");
        hub.Should()
            .NotContain("ports:", "the sidecar owns the namespace, so the port is published on it");
        sidecar.Should().Contain("ports:\n      - \"127.0.0.1:8080:8080\"");
        hub.Should().Contain("      hub-egress:\n        condition: service_healthy");
    }

    [Test]
    public void Compose_puts_the_held_service_in_its_sidecars_process_namespace()
    {
        // The sidecar refuses to serve beside a process of the egress uid, and it can see the service's processes
        // only from one PID namespace. podman compose passes pid through as written, so it names the container.
        var compose = Compose();

        Block(compose, "hub-egress").Should().Contain("container_name: orders-hub-egress");
        foreach (var service in new[] { "hub", "migrate", "drain", "resume" })
            Block(compose, service)
                .Should()
                .Contain("pid: \"container:orders-hub-egress\"", service);
        Block(compose, "worker").Should().NotContain("pid:");
    }

    [Test]
    public void Compose_keeps_every_capability_dropped_on_the_held_service()
    {
        var hub = Block(Compose(), "hub");

        hub.Should().Contain("cap_drop:\n      - ALL");
        hub.Should().NotContain("cap_add", "the app must not be able to change the rules");
    }

    [Test]
    public void Compose_holds_the_migration_to_the_migrators_egress()
    {
        var migrate = Block(Compose(), "migrate");

        migrate.Should().Contain("network_mode: \"service:hub-egress\"");
        migrate.Should().Contain("      hub-egress:\n        condition: service_healthy");
    }

    [Test]
    public void Compose_holds_the_drain_and_resume_to_the_migrators_egress()
    {
        var compose = Compose();

        Block(compose, "drain").Should().Contain("network_mode: \"service:hub-egress\"");
        Block(compose, "resume").Should().Contain("network_mode: \"service:hub-egress\"");
    }

    [Test]
    public void Compose_gives_a_service_without_egress_no_sidecar()
    {
        var compose = Compose();

        compose.Should().NotContain("worker-egress").And.NotContain("reports-egress");
        Block(compose, "worker").Should().NotContain("network_mode");
        Block(compose, "reports").Should().NotContain("network_mode");
    }

    [Test]
    public void Compose_without_egress_is_unchanged()
    {
        var file = DeployKubernetesTests.File_();
        file.Services["worker"].Count = 1;

        var compose = Compose(file);

        compose.Should().NotContain("egress").And.NotContain("network_mode").And.NotContain("pid:");
    }

    [Test]
    public void Compose_allows_only_the_database_when_no_host_uses_rabbitmq()
    {
        Block(Compose(rabbit: false), "hub-egress")
            .Should()
            .Contain(
                "TRAX_EGRESS_ALLOW: \"api.anthropic.com:443 registry.example.com:8443 tcp:database:5432\""
            );
    }

    [Test]
    public void Compose_with_an_empty_list_allows_only_the_deployments_own_services()
    {
        var file = Held();
        file.Services["hub"].Egress = [];

        Block(Compose(file), "hub-egress")
            .Should()
            .Contain("TRAX_EGRESS_ALLOW: \"tcp:database:5432 tcp:rabbitmq:5672\"");
    }

    [Test]
    public void Compose_under_docker_gives_the_sidecar_no_user_namespace_option()
    {
        Block(Compose(engine: DeployEnvironment.Docker), "hub-egress")
            .Should()
            .NotContain("userns_mode");
    }

    [Test]
    public void Compose_refuses_a_held_service_with_more_than_one_copy()
    {
        var file = Held();
        file.Services["worker"].Egress = ["api.anthropic.com"];

        FluentActions
            .Invoking(() => Compose(file))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Service 'worker' names egress and has count 2*kubernetes target*");
    }
}
