using Microsoft.EntityFrameworkCore;

namespace Trax.Samples.PersistedOperations.Models;

/// <summary>
/// EF DbContext that backs the <see cref="UserNote"/> query model. Kept
/// separate from any Trax-managed context so the sample's data model is
/// transparent and self-contained.
/// </summary>
public class UserNotesDbContext(DbContextOptions<UserNotesDbContext> options) : DbContext(options)
{
    public DbSet<UserNote> Notes { get; set; } = null!;
}
