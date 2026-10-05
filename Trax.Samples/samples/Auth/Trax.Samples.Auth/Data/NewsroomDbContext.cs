using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

namespace Trax.Samples.Auth.Data;

/// <summary>
/// The sample's own data: articles, their editor notes and the audit trail, in the
/// <c>newsroom</c> schema next to Trax's own tables.
/// </summary>
public class NewsroomDbContext(DbContextOptions<NewsroomDbContext> options)
    : DomainDataContext<NewsroomDbContext>(options),
        INewsroomDbContext
{
    public DbSet<Article> Articles => Set<Article>();

    public DbSet<EditorNote> EditorNotes => Set<EditorNote>();

    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    protected override string Schema => "newsroom";

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<Article>()
            .HasOne(a => a.EditorNote)
            .WithMany()
            .HasForeignKey(a => a.EditorNoteId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<AuditRecord>().HasIndex(a => a.PrincipalId);
    }
}
