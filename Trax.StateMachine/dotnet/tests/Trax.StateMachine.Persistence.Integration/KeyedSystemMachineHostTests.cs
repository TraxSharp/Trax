using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;
using Trax.StateMachine.Gherkin;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// A keyed system machine served from features: the host starts none of its instances, and host code starts and moves
/// each one by its key through <see cref="IMachineInstances"/>.
/// </summary>
public class KeyedSystemMachineHostTests
{
    private const string Glossary = """
        {
          "version": 1,
          "actors": { "fixer": { "noun": "a fixer", "roles": ["fixer"] } },
          "machines": {
            "ticket": {
              "system": { "keyedBy": ["id"] },
              "name": "ticket",
              "initial": "New",
              "states": { "New": "is new", "Triaged": "is in triage" },
              "triggers": { "Triage": { "phrase": "{subject} gets triaged" } }
            }
          }
        }
        """;

    private const string Tickets = """
        Feature: Tickets
          Scenario: A ticket is triaged
            Given ticket "T-12" is new
            When ticket "T-12" gets triaged
            Then ticket "T-12" is in triage
        """;

    private string _dir = null!;
    private string _file = null!;
    private ServiceProvider _provider = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-keyed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(Path.GetTempPath(), $"sm_keyed_{Guid.NewGuid():N}.db");
        var derived = FeatureMachines.Derive(
            FeatureGlossary.Read(Glossary),
            [new FeatureFile("tickets.feature", Tickets)]
        );
        derived.Succeeded.Should().BeTrue(string.Join("\n", derived.Problems));
        foreach (var (id, ir) in derived.Irs)
            File.WriteAllText(Path.Combine(_dir, id + ".ir.json"), ir);
        File.WriteAllText(Path.Combine(_dir, "host.json"), derived.Host);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTrax(trax =>
            trax.AddEffects(e => e.UseSqlite($"Data Source={_file}"))
                .AddStateMachines(o => o.ServeFeatures(_dir))
        );
        _provider = services.BuildServiceProvider();
        foreach (
            var service in _provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>()
        )
            await service.StartingAsync(CancellationToken.None);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
        try
        {
            File.Delete(_file);
        }
        catch (IOException)
        {
            // Best effort: a lingering handle can hold the temp file; the OS reaps it later.
        }
    }

    [Test]
    public async Task The_host_starts_no_instance_of_it()
    {
        await using var scope = _provider.CreateAsyncScope();

        (
            await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .SnapshotDrafts.AsNoTracking()
                .CountAsync(d => d.Machine == "ticket")
        )
            .Should()
            .Be(0);
    }

    [Test]
    public async Task Host_code_starts_and_moves_each_instance_by_its_key()
    {
        await using var scope = _provider.CreateAsyncScope();
        var instances = scope.ServiceProvider.GetRequiredService<IMachineInstances>();

        (await instances.Advance("ticket", "T-12", "Triage"))
            .Should()
            .BeOfType<AdvanceOutcome.NotFound>("nothing started it");
        var started = await instances.Start("ticket", "T-12");
        await instances.Start("ticket", "T-13");

        started.State.Should().Be("New");
        (await instances.Advance("ticket", "T-12", "Triage"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Triaged");
        (
            await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .SnapshotDrafts.AsNoTracking()
                .Where(d => d.Machine == "ticket")
                .Select(d => d.State)
                .ToListAsync()
        )
            .Should()
            .BeEquivalentTo(["Triaged", "New"]);
    }

    [Test]
    public async Task A_key_with_another_number_of_parts_is_refused()
    {
        await using var scope = _provider.CreateAsyncScope();
        var instances = scope.ServiceProvider.GetRequiredService<IMachineInstances>();
        await instances.Start("ticket", "T-12");

        var start = () => instances.Start("ticket", MachineKey.Of("T-12", "extra"));
        var advance = () => instances.Advance("ticket", MachineKey.Of("T-12", "extra"), "Triage");

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("is keyed by id");
        (await advance.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("is keyed by id");
        (
            await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .SnapshotDrafts.AsNoTracking()
                .Where(d => d.Machine == "ticket")
                .Select(d => d.State)
                .ToListAsync()
        )
            .Should()
            .BeEquivalentTo(["New"]);
    }
}
