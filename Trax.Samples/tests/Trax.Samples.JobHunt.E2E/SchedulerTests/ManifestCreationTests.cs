using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Trax.Samples.JobHunt.E2E.Fixtures;

namespace Trax.Samples.JobHunt.E2E.SchedulerTests;

[TestFixture]
public class ManifestCreationTests : JobHuntApiTestFixture
{
    [Test]
    public async Task Startup_CreatesMonitorAllActiveJobsManifest()
    {
        var manifests = await DataContext
            .Manifests.AsNoTracking()
            .Select(m => m.ExternalId)
            .ToListAsync();

        manifests.Should().Contain("MonitorAllActiveJobs");
    }

    [Test]
    public async Task Startup_ManifestExists()
    {
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ExternalId == "MonitorAllActiveJobs");

        manifest.Should().NotBeNull();
    }
}
