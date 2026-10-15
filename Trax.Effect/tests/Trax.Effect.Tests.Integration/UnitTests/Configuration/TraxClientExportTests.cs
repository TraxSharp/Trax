using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Configuration.ClientExport;

namespace Trax.Effect.Tests.Integration.UnitTests.Configuration;

/// <summary>
/// The export flag is read when it is first used, so a bad one is reported there in a sentence rather than as a type
/// initializer's failure, and a provider that skips its database for an export registers the gate that refuses to
/// serve.
/// <para>Enforces <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
public class TraxClientExportTests
{
    [TestCase("--trax-export-client")]
    [TestCase("--trax-export-client", "--urls")]
    [TestCase("--trax-export-client=")]
    public void A_bare_export_flag_is_reported_in_a_sentence(params string[] args)
    {
        var flag = new ClientExportFlag(() => ["host.dll", .. args]);

        var read = () => flag.Requested;

        read.Should()
            .Throw<ArgumentException>()
            .WithMessage(
                "The process was started with --trax-export-client and no path after it.*"
            );
    }

    [Test]
    public void The_process_flag_is_read_without_a_type_initializer()
    {
        var read = () => TraxClientExport.Requested;

        read.Should().NotThrow().Which.Should().BeFalse("the test host is not exporting");
    }

    [Test]
    public void A_provider_that_skips_its_database_registers_the_serve_gate()
    {
        var services = new ServiceCollection();

        TraxClientExport
            .SkipsDatabase(
                services,
                new ClientExportFlag(() => ["host.dll", "--trax-export-client", "out.json"])
            )
            .Should()
            .BeTrue();
        TraxClientExport
            .SkipsDatabase(services, new ClientExportFlag(() => ["host.dll"]))
            .Should()
            .BeFalse();

        services
            .Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .Should()
            .Equal(typeof(ClientExportServeGate));
        typeof(ClientExportServeGate)
            .Should()
            .NotImplement<ITraxStartupCheck>(
                "an export runs the startup checks, and the gate refuses only a host that serves"
            );
    }

    [Test]
    public async Task The_serve_gate_refuses_a_host_that_starts()
    {
        var gate = new ClientExportServeGate();

        var start = () => gate.StartingAsync(CancellationToken.None);

        await start
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*--trax-export-client*not migrated*RunTraxAsync*");
    }
}
