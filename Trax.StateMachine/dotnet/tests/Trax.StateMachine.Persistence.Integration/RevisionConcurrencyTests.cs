using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using CsCheck;
using DbUp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// Concurrent advances of one instance, on Postgres: each successful write takes the next revision, and none is
/// repeated or skipped. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
[TestFixture]
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class RevisionConcurrencyTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "u1";

    private InstanceHost _host = null!;

    [SetUp]
    public void SetUp() => _host = InstanceHost.Create(StoreProvider.Postgres);

    [TearDown]
    public void TearDown() => _host.Dispose();

    [Test]
    public async Task Twenty_concurrent_advances_leave_contiguous_revisions()
    {
        const int Callers = 20;
        var id = Guid.NewGuid();
        using (var scope = _host.Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Upsert(
                        User,
                        id,
                        new Snapshot
                        {
                            Machine = BuildMachine.Id,
                            Version = 1,
                            State = "Draft",
                            Context = new JsonObject { ["source"] = "repo" },
                        }
                    )
            ).Should().BeTrue();

        // Edit is a self-loop on Draft: every caller's advance is valid, and only the token race refuses one.
        var advanced = await Task.WhenAll(
                Enumerable
                    .Range(0, Callers)
                    .Select(_ =>
                        Task.Run(async () =>
                        {
                            for (var attempt = 0; attempt < 500; attempt++)
                            {
                                using var scope = _host.Scope();
                                var outcome = await _host
                                    .Service(scope, BuildMachine.Id)
                                    .Advance(User, id, "Edit")
                                    .ConfigureAwait(false);
                                if (outcome is AdvanceOutcome.Advanced done)
                                    return done;
                                outcome.Should().BeOfType<AdvanceOutcome.Conflict>();
                            }
                            throw new InvalidOperationException("an advance lost every race");
                        })
                    )
            )
            .WaitAsync(TimeSpan.FromSeconds(120));

        (await _host.Revision(id))
            .Should()
            .Be(1 + Callers, $"the insert and each successful advance bumped once. See {Adr}");
        advanced
            .Select(a => a.Revision)
            .Should()
            .BeEquivalentTo(
                Enumerable.Range(2, Callers).Select(r => (long?)r),
                "each successful write reports the revision it made, none repeated or skipped"
            );
    }
}
