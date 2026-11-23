using System.Data;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Trax.Effect.Data.Utils;

/// <summary>
/// Functions a LINQ query over a Trax data context hands to the database: its clock and its
/// lowering of text. Each translates to the provider's own SQL, so the database evaluates it, not
/// this process.
/// </summary>
/// <remarks>
/// They are mapped on the contexts Trax builds (<c>UsePostgres</c>, <c>UseSqlite</c>) and on every
/// <see cref="Services.DomainContext.DomainDataContext{TSelf}"/>. The in-memory provider has no
/// server; it runs each method's body in the process instead. Called outside a query, a method
/// runs its body too, which reads this process, not the database.
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/configuration/database-functions">SDK reference</seealso>
public static class DatabaseFunctions
{
    /// <summary>
    /// The database's current UTC time: <c>now()</c> on PostgreSQL (inside a transaction, the
    /// transaction's start), <c>strftime('%Y-%m-%d %H:%M:%f', 'now')</c> on SQLite. Read in a query,
    /// it is the one clock every process sharing the database agrees on.
    /// </summary>
    /// <returns>Evaluated in the process (the in-memory provider, or outside a query), the system clock's UTC time.</returns>
    public static DateTime UtcNow() => TimeProvider.System.GetUtcNow().UtcDateTime;

    /// <summary>
    /// <paramref name="value"/> lowered by the database, SQL's <c>lower()</c> on PostgreSQL and
    /// SQLite. With <see cref="LikePattern"/> it is the case-insensitive match the trigram indexes
    /// are built over.
    /// </summary>
    /// <param name="value">The text to lower; null stays null.</param>
    /// <returns>Evaluated in the process, <paramref name="value"/> lowered by the invariant culture.</returns>
    public static string? Lower(string? value) => value?.ToLowerInvariant();

    private static readonly MethodInfo UtcNowMethod =
        typeof(DatabaseFunctions).GetMethod(nameof(UtcNow), Type.EmptyTypes)
        ?? throw new InvalidOperationException("DatabaseFunctions.UtcNow is missing.");

    private static readonly MethodInfo LowerMethod =
        typeof(DatabaseFunctions).GetMethod(nameof(Lower), [typeof(string)])
        ?? throw new InvalidOperationException("DatabaseFunctions.Lower is missing.");

    private static readonly RelationalTypeMapping Text = new StringTypeMapping(
        "TEXT",
        DbType.String
    );

    /// <summary>
    /// Maps the functions for the provider named <paramref name="providerName"/>. A provider other
    /// than PostgreSQL or SQLite (the in-memory one) gets no mapping and runs the bodies.
    /// </summary>
    internal static ModelBuilder AddDatabaseFunctions(
        this ModelBuilder modelBuilder,
        string? providerName
    )
    {
        if (providerName is null)
            return modelBuilder;
        if (providerName.Contains("Npgsql", StringComparison.Ordinal))
            return modelBuilder.AddPostgresDatabaseFunctions();
        if (providerName.Contains("Sqlite", StringComparison.Ordinal))
            return modelBuilder.AddSqliteDatabaseFunctions();
        return modelBuilder;
    }

    /// <summary>Maps <see cref="UtcNow"/> to <c>now()</c> and <see cref="Lower"/> to <c>lower()</c>.</summary>
    /// <remarks>
    /// <c>now()</c> is left unqualified, as Npgsql writes it for <c>DateTime.UtcNow</c>, so it
    /// resolves through the search path.
    /// </remarks>
    internal static ModelBuilder AddPostgresDatabaseFunctions(this ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasDbFunction(UtcNowMethod)
            .HasTranslation(_ => new SqlFunctionExpression(
                "now",
                arguments: [],
                nullable: false,
                argumentsPropagateNullability: [],
                typeof(DateTime),
                typeMapping: null
            ));
        return modelBuilder.AddLower();
    }

    /// <summary>
    /// Maps <see cref="UtcNow"/> to the text EF Core's SQLite provider writes for
    /// <c>DateTime.UtcNow</c>, <c>rtrim(rtrim(strftime('%Y-%m-%d %H:%M:%f', 'now'), '0'), '.')</c>,
    /// which is the form its <see cref="DateTime"/> columns hold and so compares with them as text,
    /// and <see cref="Lower"/> to <c>lower()</c>.
    /// </summary>
    internal static ModelBuilder AddSqliteDatabaseFunctions(this ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasDbFunction(UtcNowMethod)
            .HasTranslation(_ =>
            {
                var strftime = TextFunction(
                    "strftime",
                    new SqlConstantExpression("%Y-%m-%d %H:%M:%f", Text),
                    new SqlConstantExpression("now", Text)
                );
                var zerosTrimmed = TextFunction(
                    "rtrim",
                    strftime,
                    new SqlConstantExpression("0", Text)
                );
                // Typed as the DateTime it is read as; EF gives it the provider's DateTime
                // mapping, or the column's it is compared with.
                return new SqlFunctionExpression(
                    "rtrim",
                    [zerosTrimmed, new SqlConstantExpression(".", Text)],
                    nullable: true,
                    argumentsPropagateNullability: [true, true],
                    typeof(DateTime),
                    typeMapping: null
                );
            });
        return modelBuilder.AddLower();
    }

    private static ModelBuilder AddLower(this ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasDbFunction(LowerMethod)
            .HasTranslation(args => new SqlFunctionExpression(
                "lower",
                [args[0]],
                nullable: true,
                argumentsPropagateNullability: [true],
                typeof(string),
                args[0].TypeMapping
            ));
        return modelBuilder;
    }

    private static SqlFunctionExpression TextFunction(
        string name,
        SqlExpression first,
        SqlExpression second
    ) =>
        new(
            name,
            [first, second],
            nullable: true,
            argumentsPropagateNullability: [true, true],
            typeof(string),
            Text
        );
}
