using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Startup;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// Under <c>RequirePersisted(true)</c> the allowlist starts only in Development. With enforcement
/// off, or with no allowlist, the host starts anywhere.
///
/// <para>Enforces <c>docs/adr/0005-the-allowlist-keys-on-the-caller-supplied-operation-name.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0005-the-allowlist-keys-on-the-caller-supplied-operation-name.md")]
[TestFixture]
public class AllowlistStartsOnlyInDevelopmentTests
{
    private const string Adr =
        "docs/adr/0005-the-allowlist-keys-on-the-caller-supplied-operation-name.md";

    [TestCase("Production")]
    [TestCase("Staging")]
    public async Task Required_with_named_allowlist_refuses_to_start_outside_Development(
        string environment
    )
    {
        var act = () =>
            StartAsync(environment, b => b.RequirePersisted(true).AllowOperations("HealthProbe"));

        await act.Should()
            .ThrowAsync<InvalidOperationException>(
                "under enforcement the allowlist starts only in Development, per " + Adr
            )
            .WithMessage($"*AllowOperations*'{environment}'*Development*");
    }

    [Test]
    public async Task Required_with_predicate_allowlist_refuses_to_start_outside_Development()
    {
        var act = () =>
            StartAsync(
                Environments.Production,
                b =>
                    b.RequirePersisted(true)
                        .AllowOperationsMatching(n =>
                            n.StartsWith("dev_", StringComparison.Ordinal)
                        )
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>(
                "a predicate is an allowlist as much as a name is, per " + Adr
            );
    }

    [Test]
    public async Task Required_with_allowlist_and_no_host_environment_refuses_to_start()
    {
        var options = new PersistedOperationsBuilder()
            .SingleNode()
            .RequirePersisted(true)
            .AllowOperations("HealthProbe")
            .Build();
        var gate = new PersistedOperationsAllowlistEnvironmentValidator(options);

        var act = () => gate.StartingAsync(CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>(
                "a container with no host environment is not Development, per " + Adr
            );
    }

    [Test]
    public async Task Required_with_allowlist_starts_in_Development()
    {
        var act = () =>
            StartAsync(
                Environments.Development,
                b => b.RequirePersisted(true).AllowOperations("HealthProbe")
            );

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Required_without_allowlist_starts_outside_Development()
    {
        var act = () => StartAsync(Environments.Production, b => b.RequirePersisted(true));

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Shadow_logging_with_allowlist_starts_outside_Development()
    {
        var act = () =>
            StartAsync(
                Environments.Production,
                b =>
                    b.RequirePersisted(false)
                        .LogNonPersistedRequests(true)
                        .AllowOperations("HealthProbe")
            );

        await act.Should()
            .NotThrowAsync("with enforcement off the allowlist only quiets the shadow log");
    }

    [Test]
    public void UsePersistedOperations_registers_the_gate()
    {
        var services = new ServiceCollection();
        new TraxGraphQLBuilder(services).UsePersistedOperations(b => b.SingleNode());

        services
            .Should()
            .Contain(d =>
                d.ImplementationType == typeof(PersistedOperationsAllowlistEnvironmentValidator)
            );
    }

    private static async Task StartAsync(
        string environment,
        Action<PersistedOperationsBuilder> configure
    )
    {
        var builder = new PersistedOperationsBuilder().SingleNode();
        configure(builder);
        var gate = new PersistedOperationsAllowlistEnvironmentValidator(
            builder.Build(),
            new Environment(environment)
        );
        await gate.StartingAsync(CancellationToken.None);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
