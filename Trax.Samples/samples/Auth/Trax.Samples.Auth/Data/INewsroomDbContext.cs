using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

namespace Trax.Samples.Auth.Data;

/// <summary>Companion interface for <see cref="NewsroomDbContext"/>.</summary>
public interface INewsroomDbContext : IDomainDataContext
{
    DbSet<Article> Articles { get; }

    DbSet<EditorNote> EditorNotes { get; }

    DbSet<AuditRecord> AuditRecords { get; }
}
