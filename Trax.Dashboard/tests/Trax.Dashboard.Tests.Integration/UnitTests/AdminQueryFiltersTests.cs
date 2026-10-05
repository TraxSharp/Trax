using AwesomeAssertions;
using Trax.Dashboard.Utilities;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Dashboard.Tests.Integration.UnitTests;

[TestFixture]
public class AdminQueryFiltersTests
{
    [Test]
    public void ExcludeAdmin_Metadata_FiltersAdminTrainsByFullName()
    {
        var items = new List<Metadata>
        {
            new() { Name = typeof(IManifestManagerTrain).FullName! },
            new() { Name = "MyApp.UserRegistrationTrain" },
            new() { Name = typeof(IJobDispatcherTrain).FullName! },
            new() { Name = "MyApp.OrderProcessingTrain" },
        };

        var filtered = items.AsQueryable().ExcludeAdmin(AdminTrains.FullNames).ToList();

        filtered.Should().HaveCount(2);
        filtered.Should().OnlyContain(m => m.Name.Contains("MyApp"));
    }

    [Test]
    public void ExcludeAdmin_KeepsATrainWhoseNameOnlyEndsLikeAnAdminTrain()
    {
        var items = new List<Metadata>
        {
            new() { Name = "MyApp.IBatchJobDispatcherTrain" },
            new() { Name = "MyApp.Custom.ManifestManagerTrain" },
        };

        var filtered = items.AsQueryable().ExcludeAdmin(AdminTrains.FullNames).ToList();

        filtered.Should().HaveCount(2, "only an admin train's exact FullName is hidden");
    }

    [Test]
    public void ExcludeAdmin_Manifest_FiltersAdminTrainsByFullName()
    {
        var items = new List<Manifest>
        {
            new() { Name = typeof(IManifestManagerTrain).FullName! },
            new() { Name = "MyApp.UserRegistrationTrain" },
        };

        var filtered = items.AsQueryable().ExcludeAdmin(AdminTrains.FullNames).ToList();

        filtered.Should().ContainSingle().Which.Name.Should().Contain("UserRegistration");
    }

    [Test]
    public void ExcludeAdmin_EmptyAdminNames_ReturnsAll()
    {
        var items = new List<Metadata>
        {
            new() { Name = "TrainA" },
            new() { Name = "TrainB" },
        };

        var filtered = items.AsQueryable().ExcludeAdmin(new List<string>()).ToList();

        filtered.Should().HaveCount(2);
    }
}
