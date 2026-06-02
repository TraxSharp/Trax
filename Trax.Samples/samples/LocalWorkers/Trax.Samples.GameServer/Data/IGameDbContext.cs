using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.GameServer.Data.Models;

namespace Trax.Samples.GameServer.Data;

/// <summary>Companion interface for <see cref="GameDbContext"/>.</summary>
public interface IGameDbContext : IDomainDataContext
{
    DbSet<PlayerRecord> Players { get; }
    DbSet<MatchRecord> Matches { get; }
    DbSet<PublicAnnouncement> Announcements { get; }
}
