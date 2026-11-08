using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine.Persistence.FromIr;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// A machine served from its features steps a stored row up with the migration its host file names (ADR
/// <c>Trax.Docs/adr/0058</c>): a version 1 checkout, which had no total, reads as version 2 with its total priced.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class MigrationBindingTests(StoreProvider provider)
{
    private InstanceHost _host = null!;

    [OneTimeSetUp]
    public void SetUp()
    {
        var machine = FeatureCheckout.Served();
        _host = InstanceHost.Create(
            provider,
            services: s =>
            {
                s.AddIrMachines([machine]);
                s.AddSingleton<IFeatureCharge>(new CountingCharge());
            }
        );
    }

    [OneTimeTearDown]
    public void TearDown() => _host.Dispose();

    [Test]
    public async Task A_stored_version_1_row_reads_as_version_2_with_its_total_backfilled()
    {
        var user = $"ada-{Guid.NewGuid():N}";
        var id = Guid.NewGuid();
        await _host.Seed(
            user,
            id,
            new Snapshot
            {
                Machine = "feature-checkout",
                Version = 1,
                State = "Cart",
                Context = new JsonObject
                {
                    ["items"] = new JsonArray("bread", "jam"),
                    ["receipt"] = null,
                },
            }
        );

        using var scope = _host.Scope();
        var loaded = await _host.Service(scope, "feature-checkout").Load(user, id);

        var snapshot = loaded.Should().BeOfType<LoadResult.Loaded>().Subject.Snapshot;
        snapshot.Version.Should().Be(2);
        snapshot.Context["total"]!
            .GetValue<long>()
            .Should()
            .Be(2 * FeatureCheckout.UnitPrice, "the host's migration priced the items");
    }
}
