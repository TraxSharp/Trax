using Microsoft.EntityFrameworkCore;
using Trax.Samples.GameServer.Data.Models;

namespace Trax.Samples.GameServer.Data;

/// <summary>
/// Application DbContext for game-specific data (players, matches).
/// Entities marked with [TraxQueryModel] are automatically exposed as
/// GraphQL queries when registered via AddTraxGraphQL(g => g.AddDbContext&lt;GameDbContext&gt;()).
/// </summary>
public class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<PlayerRecord> Players { get; set; } = null!;
    public DbSet<MatchRecord> Matches { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("game");

        modelBuilder.Entity<PlayerRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.PlayerId).IsUnique();
        });

        modelBuilder.Entity<MatchRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.MatchId).IsUnique();
        });
    }
}
