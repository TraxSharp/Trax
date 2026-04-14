using Microsoft.EntityFrameworkCore;
using Trax.Samples.JobHunt.Data.Entities;

namespace Trax.Samples.JobHunt.Data;

public class JobHuntDbContext(DbContextOptions<JobHuntDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();
    public DbSet<JobSnapshot> JobSnapshots => Set<JobSnapshot>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<EmailDraft> EmailDrafts => Set<EmailDraft>();
    public DbSet<EmailSent> EmailsSent => Set<EmailSent>();
    public DbSet<WatchedCompany> WatchedCompanies => Set<WatchedCompany>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ApiKey).IsRequired();
            entity.Property(e => e.DisplayName).IsRequired();
            entity.HasIndex(e => e.ApiKey).IsUnique();
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.Company).IsRequired();
            entity.Property(e => e.RawDescription).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.Status });
        });

        modelBuilder.Entity<Profile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.HasIndex(e => e.UserId).IsUnique();
        });

        modelBuilder.Entity<JobSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ContentHash).IsRequired();
            entity.Property(e => e.RawContent).IsRequired();
            entity.HasIndex(e => new { e.JobId, e.FetchedAt });
        });

        modelBuilder.Entity<Artifact>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.ModelUsed).IsRequired();
            entity.Property(e => e.Type).HasConversion<string>();
            entity.HasIndex(e => new { e.JobId, e.UserId });
        });

        modelBuilder.Entity<Contact>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Email).IsRequired();
            entity.Property(e => e.Source).IsRequired();
            entity.HasIndex(e => e.JobId);
        });

        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>();
            entity.HasIndex(e => new { e.UserId, e.Status });
        });

        modelBuilder.Entity<EmailDraft>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Subject).IsRequired();
            entity.Property(e => e.Body).IsRequired();
        });

        modelBuilder.Entity<EmailSent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Provider).IsRequired();
            entity.Property(e => e.MessageId).IsRequired();
        });

        modelBuilder.Entity<WatchedCompany>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.CompanyName).IsRequired();
            entity.Property(e => e.CareersUrl).IsRequired();
            entity.HasIndex(e => e.UserId);
        });
    }
}
