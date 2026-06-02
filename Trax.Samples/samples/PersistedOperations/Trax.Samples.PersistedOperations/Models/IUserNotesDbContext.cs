using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;

namespace Trax.Samples.PersistedOperations.Models;

/// <summary>Companion interface for <see cref="UserNotesDbContext"/>.</summary>
public interface IUserNotesDbContext : IDomainDataContext
{
    DbSet<UserNote> Notes { get; }
}
