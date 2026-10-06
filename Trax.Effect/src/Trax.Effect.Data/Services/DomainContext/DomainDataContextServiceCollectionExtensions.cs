using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Effect.Data.Services.DomainContext;

/// <summary>
/// Registration and schema-bootstrap helpers for <see cref="DomainDataContext{TSelf}"/>-derived
/// domain contexts.
/// </summary>
public static class DomainDataContextServiceCollectionExtensions
{
    /// <summary>
    /// Registers a domain data context behind its companion interface, using a pooled context factory
    /// plus a scoped resolver bound to <typeparamref name="TInterface"/>.
    /// </summary>
    /// <typeparam name="TInterface">The companion interface, e.g. <c>ICatalogDbContext</c>.</typeparam>
    /// <typeparam name="TContext">The concrete context, e.g. <c>CatalogDbContext</c>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureProvider">
    /// Configures the EF provider, e.g. <c>options =&gt; options.UseNpgsql(connectionString)</c>.
    /// </param>
    /// <remarks>
    /// Pooling keeps per-request allocation low; the scoped resolver hands application code a
    /// short-lived instance through the interface. This shape (factory + scoped resolver) avoids the
    /// duplicate-options registration error from combining <c>AddPooledDbContextFactory</c> with
    /// <c>AddDbContext</c> for the same type.
    /// </remarks>
    public static IServiceCollection AddDomainDataContext<TInterface, TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureProvider
    )
        where TContext : DbContext, TInterface
        where TInterface : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureProvider);

        services.AddPooledDbContextFactory<TContext>(configureProvider);
        services.AddScoped<TInterface>(sp =>
            sp.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContext()
        );

        return services;
    }

    /// <summary>
    /// Creates the context's schema and whichever of its tables do not exist yet, at startup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Domain tables typically share a database with the Trax framework tables, so <c>EnsureCreated</c>
    /// would see the database already populated and create nothing. Instead, on a relational provider
    /// this creates the default schema (when set), checks which of the model's tables already exist,
    /// and runs the create statements for the rest, in one transaction: each missing table with its
    /// keys, indexes, foreign keys and seed rows. A table that exists is left exactly as it is, so a
    /// second run over a created schema does nothing, and a table added to the model later is created
    /// on the next start. On the in-memory provider it falls back to <c>EnsureCreated</c>.
    /// </para>
    /// <para>
    /// A statement the database refuses is not swallowed: its <see cref="DbException"/> propagates
    /// and the transaction rolls back, so a real DDL error stops the host at startup instead of
    /// leaving it running without its tables. A sequence is checked and created like a table.
    /// Statements tied to no table or sequence (a database extension, a Postgres enum) run only when
    /// none of the context's own tables, the ones in its default schema, exist yet; a table it maps
    /// from another schema does not count.
    /// </para>
    /// <para>
    /// On PostgreSQL the whole check-and-create runs under a session advisory lock, so hosts that
    /// start together against a fresh database create the schema and tables once, one after another,
    /// rather than racing each other's DDL.
    /// </para>
    /// <para>
    /// It compares tables by name only: a column added to an existing table's model is not added to
    /// the table. This is a convenience bootstrap; production apps should use real migrations.
    /// </para>
    /// </remarks>
    /// <exception cref="DbException">The database refused a create statement.</exception>
    public static async Task EnsureSchemaCreatedAsync<TContext>(
        this IServiceProvider services,
        CancellationToken cancellationToken = default
    )
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TContext>>();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        if (!db.Database.IsRelational())
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        var schema = db.Model.GetDefaultSchema();
        if (!string.IsNullOrEmpty(schema) && !IsSafeIdentifier(schema))
            throw new InvalidOperationException(
                $"Schema name '{schema}' is not a valid SQL identifier. Use letters, digits, "
                    + "and underscores only, starting with a letter or underscore."
            );

        // The operations GenerateCreateScript would script, so the statements are the ones the
        // provider writes for this model, less those for objects that are already there.
        var model = db.GetService<IDesignTimeModel>().Model;
        var operations = db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, model.GetRelationalModel());

        var serialized = IsPostgres(db);

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            // Hosts that start together would otherwise all see the same objects missing and race
            // to create them, and every one but the first would fail on the catalog's unique index.
            // The lock is held from before the schema is created until the creates commit, so the
            // probes below see what an earlier host created.
            if (serialized)
                await AcquireBootstrapLock(db, cancellationToken);

            await CreateTheObjects(db, schema, model, operations, cancellationToken);
        }
        finally
        {
            if (serialized)
                await ReleaseBootstrapLock(db);
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task CreateTheObjects(
        DbContext db,
        string? schema,
        IModel model,
        IReadOnlyList<MigrationOperation> operations,
        CancellationToken cancellationToken
    )
    {
        if (!string.IsNullOrEmpty(schema))
        {
            // A schema name cannot be a SQL parameter (it is an identifier, not a value). The value
            // is the context's own compile-time schema constant, never user input, and is
            // identifier-validated above, so the EF1002 interpolation warning does not apply.
#pragma warning disable EF1002
            await db.Database.ExecuteSqlRawAsync(
                $"CREATE SCHEMA IF NOT EXISTS \"{schema}\";",
                cancellationToken
            );
#pragma warning restore EF1002
        }

        var existing = await ExistingObjects(db, operations, cancellationToken);

        // Whether this is the context's first creation is decided by its own tables, the ones in
        // its default schema. A table it maps from another context's schema (a cross-schema read)
        // can already exist when this context has never been created.
        var anyOwnTableExisted = operations
            .OfType<CreateTableOperation>()
            .Any(create =>
                create.Schema == schema && existing.Contains((create.Name, create.Schema))
            );

        var missing = operations
            .Where(operation =>
                operation switch
                {
                    EnsureSchemaOperation => true,
                    CreateSequenceOperation sequence => !existing.Contains(
                        (sequence.Name, sequence.Schema)
                    ),
                    _ => TableOf(operation) is { } table
                        ? !existing.Contains(table)
                        : !anyOwnTableExisted,
                }
            )
            .ToList();

        if (missing.All(operation => operation is EnsureSchemaOperation))
            return;

        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(missing, model);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var command in commands)
            await db.Database.ExecuteSqlRawAsync(command.CommandText, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>The table an operation creates or writes to, or null for one tied to no table.</summary>
    private static (string Name, string? Schema)? TableOf(MigrationOperation operation) =>
        operation switch
        {
            CreateTableOperation create => (create.Name, create.Schema),
            ITableMigrationOperation onTable => (onTable.Table, onTable.Schema),
            _ => null,
        };

    /// <summary>
    /// The tables and sequences the operations create that already exist. Each is probed with a
    /// query that reads no rows (a sequence is a relation that can be selected from too), sent on
    /// the connection directly so a missing one is not logged as a failed command.
    /// </summary>
    private static async Task<HashSet<(string Name, string? Schema)>> ExistingObjects(
        DbContext db,
        IReadOnlyList<MigrationOperation> operations,
        CancellationToken cancellationToken
    )
    {
        var sql = db.GetService<ISqlGenerationHelper>();
        var connection = db.Database.GetDbConnection();
        var existing = new HashSet<(string Name, string? Schema)>();

        var created = operations
            .Select(operation =>
                operation switch
                {
                    CreateTableOperation table => ((string Name, string? Schema)?)
                        (table.Name, table.Schema),
                    CreateSequenceOperation sequence => (sequence.Name, sequence.Schema),
                    _ => null,
                }
            )
            .OfType<(string Name, string? Schema)>();

        foreach (var (name, objectSchema) in created)
        {
            await using var probe = connection.CreateCommand();
            probe.CommandText =
                $"SELECT 1 FROM {sql.DelimitIdentifier(name, objectSchema)} WHERE 1 = 0";
            try
            {
                await probe.ExecuteNonQueryAsync(cancellationToken);
                existing.Add((name, objectSchema));
            }
            catch (DbException)
            {
                // The table is not there (or cannot be read, in which case creating it fails
                // loudly below).
            }
        }

        return existing;
    }

    /// <summary>
    /// The two-key advisory lock the bootstrap holds on Postgres: a fixed class key and 0. One key
    /// for every context, because a context that maps another context's table (a cross-schema read)
    /// creates it too when it is missing. The two-key space is apart from the single-key one, and
    /// the class key differs from the migration lock's.
    /// </summary>
    private const string BootstrapLockKey = "hashtext('trax_domain_schema'), 0";

    private static readonly TimeSpan BootstrapLockPollInterval = TimeSpan.FromMilliseconds(100);

    // PostgreSQL is the only provider here with concurrent DDL from several hosts to serialize:
    // SQLite takes one writer at a time and the in-memory provider never reaches this path. Probed
    // by provider name, as DomainDataContext does, to keep Npgsql out of the provider-neutral path.
    private static bool IsPostgres(DbContext db) =>
        db.Database.ProviderName is { } provider
        && provider.Contains("Npgsql", StringComparison.Ordinal);

    /// <summary>
    /// Takes the bootstrap lock, polling with <c>pg_try_advisory_lock</c> rather than waiting inside
    /// <c>pg_advisory_lock</c>, so a waiting host holds no snapshot (see the migration lock).
    /// </summary>
    private static async Task AcquireBootstrapLock(
        DbContext db,
        CancellationToken cancellationToken
    )
    {
        var connection = db.Database.GetDbConnection();
        while (true)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT pg_try_advisory_lock({BootstrapLockKey});";
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
                return;

            await Task.Delay(BootstrapLockPollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// Releases the bootstrap lock. A session lock outlives a transaction, and the pooled connection
    /// would carry it back to the pool, so it is released explicitly; a connection that broke has
    /// already lost it.
    /// </summary>
    private static async Task ReleaseBootstrapLock(DbContext db)
    {
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"SELECT pg_advisory_unlock({BootstrapLockKey});";
            await command.ExecuteScalarAsync(CancellationToken.None);
        }
        catch (DbException)
        {
            // The connection is gone, and the lock with it.
        }
    }

    private static bool IsSafeIdentifier(string value)
    {
        if (value.Length == 0)
            return false;

        if (value[0] != '_' && !char.IsLetter(value[0]))
            return false;

        foreach (var c in value)
        {
            if (c != '_' && !char.IsLetterOrDigit(c))
                return false;
        }

        return true;
    }
}
