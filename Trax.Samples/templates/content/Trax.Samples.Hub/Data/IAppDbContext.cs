using Microsoft.EntityFrameworkCore;
using Trax.Samples.Hub.Data.Models;

namespace Trax.Samples.Hub.Data;

/// <summary>
/// Companion interface for <see cref="AppDbContext"/>. Application code (junctions, services) depends
/// on the interface, never the concrete context.
/// </summary>
public interface IAppDbContext
{
    DbSet<Note> Notes { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
