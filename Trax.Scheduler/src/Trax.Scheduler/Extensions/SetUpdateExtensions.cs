using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// Whether a data context can run a set-based <c>ExecuteUpdate</c> or <c>ExecuteDelete</c>.
/// </summary>
/// <remarks>
/// Those are relational: the InMemory provider throws on them. The operations surface supports
/// InMemory hosts, so each of its set updates checks this and, when it is false, loads the rows
/// the statement would have touched, changes them and saves, which gives the same result without
/// the single statement's guarantee against a concurrent writer (InMemory has one process).
/// </remarks>
internal static class SetUpdateExtensions
{
    internal static bool SupportsSetUpdates(this IDataContext context) =>
        context is DbContext db && db.Database.IsRelational();

    /// <summary>
    /// The per-row form of a set update: loads the rows <paramref name="rows"/> selects, applies
    /// <paramref name="change"/> to each and saves once.
    /// </summary>
    /// <returns>The number of rows changed, as <c>ExecuteUpdate</c> would report it.</returns>
    internal static async Task<int> UpdateEachAsync<T>(
        this IDataContext context,
        IQueryable<T> rows,
        Action<T> change,
        CancellationToken ct
    )
    {
        var loaded = await rows.ToListAsync(ct);
        foreach (var row in loaded)
            change(row);
        await context.SaveChanges(ct);
        return loaded.Count;
    }
}
