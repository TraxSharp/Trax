using FluentAssertions;
using Trax.Dashboard.Utilities;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;

namespace Trax.Dashboard.Tests.Integration.UnitTests;

[TestFixture]
public class AdminQueryFiltersTests
{
    [Test]
    public void ExcludeAdmin_Metadata_FiltersAdminNames()
    {
        // Arrange
        var items = new List<Metadata>
        {
            new() { Name = "Trax.Scheduler.ManifestManagerTrain" },
            new() { Name = "MyApp.UserRegistrationTrain" },
            new() { Name = "Trax.Scheduler.JobDispatcherTrain" },
            new() { Name = "MyApp.OrderProcessingTrain" },
        };
        var adminNames = new List<string> { "ManifestManagerTrain", "JobDispatcherTrain" };

        // Act
        var filtered = items.AsQueryable().ExcludeAdmin(adminNames).ToList();

        // Assert
        filtered.Should().HaveCount(2);
        filtered.Should().OnlyContain(m => m.Name.Contains("MyApp"));
    }

    [Test]
    public void ExcludeAdmin_Manifest_FiltersAdminNames()
    {
        // Arrange
        var items = new List<Manifest>
        {
            new() { Name = "Trax.Scheduler.ManifestManagerTrain" },
            new() { Name = "MyApp.UserRegistrationTrain" },
        };
        var adminNames = new List<string> { "ManifestManagerTrain" };

        // Act
        var filtered = items.AsQueryable().ExcludeAdmin(adminNames).ToList();

        // Assert
        filtered.Should().HaveCount(1);
        filtered[0].Name.Should().Contain("UserRegistration");
    }

    [Test]
    public void ExcludeAdmin_EmptyAdminNames_ReturnsAll()
    {
        // Arrange
        var items = new List<Metadata>
        {
            new() { Name = "TrainA" },
            new() { Name = "TrainB" },
        };

        // Act
        var filtered = items.AsQueryable().ExcludeAdmin(new List<string>()).ToList();

        // Assert
        filtered.Should().HaveCount(2);
    }
}
