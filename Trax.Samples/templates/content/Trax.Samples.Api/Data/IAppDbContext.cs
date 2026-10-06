using Microsoft.EntityFrameworkCore;
using Trax.Samples.Api.Data.Models;

namespace Trax.Samples.Api.Data;

/// <summary>
/// Companion interface for <see cref="AppDbContext"/>. Application code (junctions, services) depends
/// on the interface, never the concrete context.
/// </summary>
public interface IAppDbContext
{
    DbSet<Note> Notes { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
