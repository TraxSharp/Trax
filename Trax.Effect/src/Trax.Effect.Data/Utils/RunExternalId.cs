using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;

namespace Trax.Effect.Data.Utils;

/// <summary>
/// A text column read as a run's external id, so a query comparing it with
/// <c>trax.metadata.external_id</c> inside one statement keeps <c>ix_metadata_external_id</c>.
/// </summary>
/// <remarks>
/// <c>metadata.external_id</c> is <c>character(32)</c>; the columns that hold a run's external id elsewhere
/// (<c>snapshot_draft.invoke_token</c>, <c>spawned_by</c>) are <c>text</c>. Postgres compares the two as text, casting
/// the run's column, which no index on it serves, so a correlated lookup reads every run for every row. Through
/// <see cref="Of"/> Postgres casts the text side to <c>character(32)</c> instead. A cast truncates past 32
/// characters, so a caller that needs an exact match keeps its plain comparison beside this one: the cast finds the
/// runs through the index, the plain comparison decides.
/// </remarks>
internal static class RunExternalId
{
    /// <summary>
    /// <paramref name="value"/> as a run's external id. Postgres reads it as <c>character(32)</c>; SQLite, which has
    /// no such type, and a provider that evaluates in memory read it unchanged.
    /// </summary>
    internal static string? Of(string? value) => value;

    private static readonly MethodInfo OfMethod =
        typeof(RunExternalId).GetMethod(nameof(Of), BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("RunExternalId.Of is missing.");

    private static readonly RelationalTypeMapping Character32 = new StringTypeMapping(
        "character(32)",
        DbType.StringFixedLength
    );

    /// <summary>Maps <see cref="Of"/> to a cast to <c>character(32)</c>.</summary>
    internal static ModelBuilder AddRunExternalIdCast(this ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasDbFunction(OfMethod)
            .HasTranslation(args => new SqlUnaryExpression(
                ExpressionType.Convert,
                args[0],
                typeof(string),
                Character32
            ));
        return modelBuilder;
    }

    /// <summary>Maps <see cref="Of"/> to its argument unchanged.</summary>
    internal static ModelBuilder AddRunExternalIdUnchanged(this ModelBuilder modelBuilder)
    {
        modelBuilder.HasDbFunction(OfMethod).HasTranslation(args => args[0]);
        return modelBuilder;
    }
}
