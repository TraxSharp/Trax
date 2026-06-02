using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.GameServer.Data.Models;

namespace Trax.Samples.GameServer.Data;

/// <summary>
/// Application DbContext for game-specific data (players, matches). Owns the <c>game</c> schema.
/// Entities marked with [TraxQueryModel] are automatically exposed as GraphQL queries when
/// registered via AddTraxGraphQL(g => g.AddDbContext&lt;GameDbContext&gt;()).
/// </summary>
public class GameDbContext(DbContextOptions<GameDbContext> options)
    : DomainDataContext<GameDbContext>(options),
        IGameDbContext
{
    public DbSet<PlayerRecord> Players { get; set; } = null!;
    public DbSet<MatchRecord> Matches { get; set; } = null!;
    public DbSet<PublicAnnouncement> Announcements { get; set; } = null!;

    protected override string Schema => "game";

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
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

        modelBuilder.Entity<PublicAnnouncement>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity
                .HasOne(p => p.RelatedMatch)
                .WithMany()
                .HasForeignKey(p => p.RelatedMatchId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
