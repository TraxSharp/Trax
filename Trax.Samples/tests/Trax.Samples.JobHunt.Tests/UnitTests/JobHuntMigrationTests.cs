using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Trax.Samples.JobHunt.Data;

namespace Trax.Samples.JobHunt.Tests.UnitTests;

/// <summary>
/// Guards against model/migration drift. If a property, schema, or converter changes on
/// <see cref="JobHuntDbContext"/> without a matching migration, the Hub's <c>MigrateAsync</c> trips
/// EF's <c>PendingModelChangesWarning</c> at startup and every JobHunt.E2E test fails in OneTimeSetUp.
/// This catches the same drift at unit-test time, with no database: <c>HasPendingModelChanges</c>
/// compares the live model against the latest snapshot in memory.
/// </summary>
[TestFixture]
public class JobHuntMigrationTests
{
    [Test]
    public void Model_MatchesLatestMigrationSnapshot()
    {
        using var context = new JobHuntDbContextFactory().CreateDbContext([]);

        context
            .Database.HasPendingModelChanges()
            .Should()
            .BeFalse(
                "JobHuntDbContext's model has drifted from its latest migration snapshot. Run "
                    + "`dotnet ef migrations add <Name> "
                    + "--project samples/JobHunt/Trax.Samples.JobHunt.Data "
                    + "--startup-project samples/JobHunt/Trax.Samples.JobHunt.Data` to capture the change."
            );
    }
}
