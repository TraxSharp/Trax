using Microsoft.EntityFrameworkCore;
using Trax.Samples.JobHunt.Data;

namespace Trax.Samples.JobHunt.Tests.Fixtures;

/// <summary>
/// Static factory for unit tests that need a JobHuntDbContext without standing
/// up the full host. Uses EF Core's in-memory provider with a per-test database
/// name so tests are fully isolated.
///
/// This is appropriate for unit tests of pure junction logic. End-to-end tests
/// that exercise Postgres-specific behavior live in Trax.Samples.JobHunt.E2E.
/// </summary>
public static class JobHuntDbContextFixture
{
    public static JobHuntDbContext Create()
    {
        var options = new DbContextOptionsBuilder<JobHuntDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new JobHuntDbContext(options);
    }
}
