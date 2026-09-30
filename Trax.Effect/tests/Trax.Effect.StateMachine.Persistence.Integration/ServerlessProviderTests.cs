using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// <c>AddStateMachines</c> on the server-free providers (InMemory, SQLite): the subsystem stores drafts through
/// the data context the provider registers, with no host <c>AddDbContext</c> call and no context of its own.
/// </summary>
[TestFixture]
public class ServerlessProviderTests
{
    private readonly List<string> _tempFiles = [];

    [TearDown]
    public void CleanUp()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var file in _tempFiles)
            if (File.Exists(file))
                File.Delete(file);
        _tempFiles.Clear();
    }

    [Test]
    public async Task InMemory_stores_a_draft_through_its_data_context()
    {
        await AssertStoresADraft(effects => effects.UseInMemory(), expectedProvider: "InMemory");
    }

    [Test]
    public async Task Sqlite_stores_a_draft_through_its_data_context()
    {
        var file = Path.Combine(Path.GetTempPath(), $"trax-sm-{Guid.NewGuid():N}.db");
        _tempFiles.Add(file);

        await AssertStoresADraft(
            effects => effects.UseSqlite($"Data Source={file}"),
            expectedProvider: "Sqlite"
        );
    }

    [Test]
    public void AddStateMachines_without_a_data_provider_throws_a_helpful_error()
    {
        var services = new ServiceCollection();
        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(e => e).AddStateMachines(typeof(ServerlessProviderTests).Assembly)
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*requires a data provider*");
    }

    private static async Task AssertStoresADraft(
        Func<TraxEffectBuilder, TraxEffectBuilderWithData> provider,
        string expectedProvider
    )
    {
        var services = new ServiceCollection();
        services.AddScoped<ISnapshotPrincipal>(_ => new FakePrincipal("u1"));
        services.AddScoped<IOrderCharge, CountingEffect>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => provider(effects))
                .AddStateMachines(typeof(OrderMachine).Assembly)
        );
        await using var host = services.BuildServiceProvider();
        await using var scope = host.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var id = Guid.NewGuid();
        (
            await sp.GetRequiredService<ISnapshotMachineRegistry>()
                .Service("order")!
                .Autosave("u1", id, OrderMachine.ReviewSnapshot(1))
        )
            .Should()
            .BeOfType<AutosaveResult.Saved>();

        var db = sp.GetRequiredService<IDataContext>();
        ((DbContext)db).Database.ProviderName.Should().Contain(expectedProvider);
        var back = db.SnapshotDrafts.AsNoTracking().Single(x => x.UserKey == "u1" && x.Id == id);
        back.State.Should().Be("Review");
        back.Machine.Should().Be("order");
    }
}
