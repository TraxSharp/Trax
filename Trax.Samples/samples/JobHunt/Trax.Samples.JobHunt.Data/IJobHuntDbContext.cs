using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.JobHunt.Data.Entities;

namespace Trax.Samples.JobHunt.Data;

/// <summary>Companion interface for <see cref="JobHuntDbContext"/>.</summary>
public interface IJobHuntDbContext : IDomainDataContext
{
    DbSet<User> Users { get; }
    DbSet<Job> Jobs { get; }
    DbSet<Profile> Profiles { get; }
    DbSet<Artifact> Artifacts { get; }
    DbSet<JobSnapshot> JobSnapshots { get; }
    DbSet<Contact> Contacts { get; }
    DbSet<Application> Applications { get; }
    DbSet<EmailDraft> EmailDrafts { get; }
    DbSet<EmailSent> EmailsSent { get; }
    DbSet<WatchedCompany> WatchedCompanies { get; }
}
