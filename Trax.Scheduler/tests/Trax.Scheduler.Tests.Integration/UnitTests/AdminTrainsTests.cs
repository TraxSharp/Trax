using FluentAssertions;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class AdminTrainsTests
{
    [Test]
    public void Types_ContainsExpectedCount()
    {
        // Admin trains should include ManifestManagerTrain, JobDispatcherTrain,
        // TaskServerExecutorTrain, MetadataCleanupTrain and their interfaces
        AdminTrains.Types.Should().NotBeEmpty();
        AdminTrains.Types.Count.Should().BeGreaterThanOrEqualTo(4);
    }

    [Test]
    public void FullNames_MatchTypes()
    {
        // Assert — each FullName should correspond to a type in Types
        AdminTrains.FullNames.Should().HaveCount(AdminTrains.Types.Count);

        foreach (var type in AdminTrains.Types)
        {
            AdminTrains.FullNames.Should().Contain(type.FullName!);
        }
    }

    [Test]
    public void ShortNames_MatchTypes()
    {
        // Assert — each ShortName should correspond to a type Name in Types
        AdminTrains.ShortNames.Should().HaveCount(AdminTrains.Types.Count);

        foreach (var type in AdminTrains.Types)
        {
            AdminTrains.ShortNames.Should().Contain(type.Name);
        }
    }
}
