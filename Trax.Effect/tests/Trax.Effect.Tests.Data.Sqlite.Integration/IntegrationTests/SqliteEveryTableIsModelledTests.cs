using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Tests.Data.Sqlite.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>
/// The Sqlite half of the Postgres <c>EveryTableIsModelledTests</c>: every table the Sqlite migrations
/// create is mapped by the data context, and every column the context maps exists in the migrated
/// table. A feature package's table ships with its model, persistent mapping and <c>DbSet</c>, so the
/// feature reaches it through <see cref="IDataContext"/> rather than through SQL of its own.
///
/// <para>Enforces Trax.Docs/adr/0036-a-feature-table-ships-with-its-model-in-effect.md.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0036-a-feature-table-ships-with-its-model-in-effect.md")]
public class SqliteEveryTableIsModelledTests : TestSetup
{
    private const string Adr =
        "Trax.Docs/adr/0036-a-feature-table-ships-with-its-model-in-effect.md";

    /// <summary>
    /// Tables that are not on the data context, each with the reason.
    /// </summary>
    private static readonly Dictionary<string, string> Unmodelled = new(StringComparer.Ordinal)
    {
        ["SchemaVersions"] = "DbUp's journal of applied scripts, not Trax data",
    };

    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services.BuildServiceProvider();

    private DbContext NewContext() =>
        (DbContext)Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>().Create();

    [Test]
    public async Task Every_migrated_table_is_mapped_by_the_data_context()
    {
        using var context = NewContext();
        var migrated = await MigratedColumns(context);
        var mapped = MappedColumns(context);

        migrated
            .Keys.Where(table => !mapped.ContainsKey(table) && !Unmodelled.ContainsKey(table))
            .Order()
            .Should()
            .BeEmpty(
                "a table in the core migration set ships with its model, persistent mapping and "
                    + "DbSet on IDataContext, so a feature package reaches it through the data "
                    + $"context and not through SQL of its own. See {Adr}."
            );
    }

    [Test]
    public async Task Every_mapped_column_exists_in_its_migrated_table()
    {
        using var context = NewContext();
        var migrated = await MigratedColumns(context);
        var mapped = MappedColumns(context);

        mapped
            .SelectMany(table =>
                table
                    .Value.Where(column =>
                        !migrated.TryGetValue(table.Key, out var columns)
                        || !columns.Contains(column)
                    )
                    .Select(column => $"{table.Key}.{column}")
            )
            .Order()
            .Should()
            .BeEmpty(
                "every column a model maps must be created by the shipped migrations, or the "
                    + $"first query that reads it fails. See {Adr}."
            );
    }

    [Test]
    public async Task Runner_nonce_is_mapped_column_for_column()
    {
        using var context = NewContext();
        var migrated = await MigratedColumns(context);
        var mapped = MappedColumns(context);

        mapped
            .Should()
            .ContainKey(
                "runner_nonce",
                $"Trax.Scheduler reaches runner_nonce through IDataContext.RunnerNonces. See {Adr}."
            );
        mapped["runner_nonce"]
            .Should()
            .BeEquivalentTo(
                migrated["runner_nonce"],
                $"the RunnerNonce model maps exactly the columns its migration creates. See {Adr}."
            );
    }

    [TestCase("snapshot_draft")]
    [TestCase("effect_claim")]
    public async Task State_machine_tables_are_mapped_column_for_column(string table)
    {
        using var context = NewContext();
        var migrated = await MigratedColumns(context);
        var mapped = MappedColumns(context);

        mapped
            .Should()
            .ContainKey(
                table,
                $"Trax.Effect.StateMachine.Persistence reaches {table} through IDataContext. See {Adr}."
            );
        mapped[table]
            .Should()
            .BeEquivalentTo(
                migrated[table],
                $"the {table} model maps exactly the columns its migrations create. See {Adr}."
            );
    }

    private static async Task<Dictionary<string, HashSet<string>>> MigratedColumns(
        DbContext context
    )
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT m.name, c.name
            FROM sqlite_master m, pragma_table_info(m.name) c
            WHERE m.type = 'table' AND m.name NOT LIKE 'sqlite_%'
            """;

        var tables = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var table = reader.GetString(0);
            if (!tables.TryGetValue(table, out var columns))
                tables[table] = columns = new HashSet<string>(StringComparer.Ordinal);
            columns.Add(reader.GetString(1));
        }

        return tables;
    }

    private static Dictionary<string, HashSet<string>> MappedColumns(DbContext context)
    {
        var tables = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entity in context.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null)
                continue;

            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            if (!tables.TryGetValue(table, out var columns))
                tables[table] = columns = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in entity.GetProperties())
                if (property.GetColumnName(store) is { } column)
                    columns.Add(column);
        }

        return tables;
    }
}
