using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The invoked-run cap counts a user's live runs on every invoking write, so its count by starter must read the index
/// migration 083 built for it rather than the older ones, and a user owner it cannot count is refused, never let past.
/// </summary>
[TestFixture]
[Property("adr", Adr)]
public class InvokedRunLimitTests
{
    private const string Adr =
        "Trax.Docs/adr/0051-only-the-caller-who-started-a-run-may-follow-its-progress.md";

    [Test]
    public async Task The_starter_count_uses_its_index()
    {
        using var host = InstanceHost.Create(StoreProvider.Postgres);
        using var scope = host.Scope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();

        var plan = await Explain(
            (DbContext)context,
            InvokeOutbox.LiveRunsStartedBy(context, new string('a', 64))
        );

        plan.Should().Contain("ix_metadata_live_invoked_starter", plan);
    }

    [TestCase(StoreProvider.Postgres, null)]
    [TestCase(StoreProvider.Postgres, " ")]
    [TestCase(StoreProvider.Sqlite, "")]
    public async Task A_user_owner_with_no_starter_does_not_skip_the_cap(
        StoreProvider provider,
        string? userKey
    )
    {
        using var host = InstanceHost.Create(provider);
        using var scope = host.Scope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<IDataContext>();

        var refused = await services
            .GetRequiredService<InvokeOutbox>()
            .OverLimit(
                context,
                new DraftOwner(SnapshotOwnerKind.User, userKey),
                null,
                int.MaxValue,
                CancellationToken.None
            );

        refused
            .Should()
            .NotBeNull("a user owner whose key names nobody cannot be counted, so it is refused");
        refused!.Code.Should().Be(InvokeOutbox.Forbidden);
    }

    private static async Task<string> Explain<T>(DbContext context, IQueryable<T> query)
    {
        await using var command = (NpgsqlCommand)query.CreateDbCommand();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        command.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();

        await using (
            var off = new NpgsqlCommand(
                "SET LOCAL enable_seqscan = off",
                command.Connection,
                command.Transaction
            )
        )
            await off.ExecuteNonQueryAsync();

        command.CommandText = "EXPLAIN " + command.CommandText;
        var lines = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                lines.Add(reader.GetString(0));

        return string.Join('\n', lines);
    }
}
