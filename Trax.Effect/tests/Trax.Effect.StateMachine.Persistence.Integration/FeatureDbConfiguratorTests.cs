using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.FeatureDbConfigurator;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// Change 2: each data provider registers an <see cref="ITraxFeatureDbConfigurator"/> so a feature's own
/// context (the <see cref="SnapshotDbContext"/>) binds to the app's database with no host <c>AddDbContext</c>
/// call. Proven end-to-end on the server-free providers (InMemory, SQLite): resolve the configurator, build a
/// SnapshotDbContext from it, and round-trip a <see cref="SnapshotRecord"/>. The InMemory case is the parity
/// edge the SnapshotDbContext only special-cases SQLite for; this proves it works there too.
/// </summary>
[TestFixture]
public class FeatureDbConfiguratorTests
{
    private readonly List<string> _tempFiles = [];

    [TearDown]
    public void CleanUp()
    {
        foreach (var file in _tempFiles)
            if (File.Exists(file))
                File.Delete(file);
        _tempFiles.Clear();
    }

    [Test]
    public void InMemory_registers_a_configurator_that_round_trips_the_snapshot_context()
    {
        var services = new ServiceCollection();
        services.AddTrax(trax => trax.AddEffects(e => e.UseInMemory()));
        using var provider = services.BuildServiceProvider();

        AssertRoundTrips(provider, expectedProvider: "InMemory");
    }

    [Test]
    public void Sqlite_registers_a_configurator_that_round_trips_the_snapshot_context()
    {
        var file = Path.Combine(Path.GetTempPath(), $"trax-featuredb-{Guid.NewGuid():N}.db");
        _tempFiles.Add(file);

        var services = new ServiceCollection();
        services.AddTrax(trax => trax.AddEffects(e => e.UseSqlite($"Data Source={file}")));
        using var provider = services.BuildServiceProvider();

        AssertRoundTrips(provider, expectedProvider: "Sqlite");
    }

    [Test]
    public void AddStateMachines_without_a_data_provider_throws_a_helpful_error()
    {
        var services = new ServiceCollection();
        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(e => e)
                    .AddStateMachines(typeof(FeatureDbConfiguratorTests).Assembly)
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*requires a data provider*");
    }

    private static void AssertRoundTrips(ServiceProvider provider, string expectedProvider)
    {
        var configurator = provider.GetRequiredService<ITraxFeatureDbConfigurator>();

        var options = new DbContextOptionsBuilder<SnapshotDbContext>();
        configurator.Configure(options);

        using var db = new SnapshotDbContext(options.Options);
        db.Database.ProviderName.Should().Contain(expectedProvider);
        db.Database.EnsureCreated();

        var id = Guid.NewGuid();
        db.SnapshotDrafts.Add(
            new SnapshotRecord
            {
                Id = id,
                UserKey = "u1",
                Machine = "turnstile",
                Version = 1,
                State = "Locked",
                Context = "{}",
                ConcurrencyToken = Guid.NewGuid(),
                UpdatedAt = DateTimeOffset.UtcNow,
            }
        );
        db.SaveChanges();

        var back = db.SnapshotDrafts.AsNoTracking().Single(x => x.UserKey == "u1" && x.Id == id);
        back.State.Should().Be("Locked");
        back.Machine.Should().Be("turnstile");
    }
}
