using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

namespace Trax.Samples.PersistedOperations.Models;

/// <summary>
/// EF DbContext that backs the <see cref="UserNote"/> query model. Owns the <c>notes</c> schema,
/// kept separate from any Trax-managed context so the sample's data model is self-contained.
/// </summary>
public class UserNotesDbContext(DbContextOptions<UserNotesDbContext> options)
    : DomainDataContext<UserNotesDbContext>(options),
        IUserNotesDbContext
{
    public DbSet<UserNote> Notes { get; set; } = null!;

    protected override string Schema => "notes";

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        // UserNote is mapped via data annotations ([Table], [Column], [Key]); nothing fluent needed.
    }
}
