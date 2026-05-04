using FluentAssertions;
using NUnit.Framework;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class ScheduleOptionsTests
{
    [Test]
    public void Priority_StoresValue()
    {
        var opts = new ScheduleOptions().Priority(7);
        opts.ToManifestOptions().Priority.Should().Be(7);
    }

    [Test]
    public void Enabled_FalseFlagsManifestDisabled()
    {
        var opts = new ScheduleOptions().Enabled(false);
        opts.ToManifestOptions().IsEnabled.Should().BeFalse();
    }

    [Test]
    public void MaxRetries_StoresValue()
    {
        var opts = new ScheduleOptions().MaxRetries(7);
        opts.ToManifestOptions().MaxRetries.Should().Be(7);
    }

    [Test]
    public void Timeout_StoresValue()
    {
        var opts = new ScheduleOptions().Timeout(TimeSpan.FromMinutes(2));
        opts.ToManifestOptions().Timeout.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Test]
    public void Dormant_FlagsAsDormant()
    {
        var opts = new ScheduleOptions().Dormant();
        opts.ToManifestOptions().IsDormant.Should().BeTrue();
    }

    [Test]
    public void OnMisfire_StoresPolicy()
    {
        var opts = new ScheduleOptions().OnMisfire(MisfirePolicy.FireOnceNow);
        opts.ToManifestOptions().MisfirePolicy.Should().Be(MisfirePolicy.FireOnceNow);
    }

    [Test]
    public void MisfireThreshold_StoresValue()
    {
        var opts = new ScheduleOptions().MisfireThreshold(TimeSpan.FromMinutes(15));
        opts.ToManifestOptions().MisfireThreshold.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Test]
    public void Exclude_AppendsExclusion()
    {
        var opts = new ScheduleOptions()
            .Exclude(Exclude.DaysOfWeek(DayOfWeek.Sunday))
            .Exclude(Exclude.TimeWindow(new TimeOnly(2, 0), new TimeOnly(4, 0)));

        opts.ToManifestOptions().Exclusions.Should().HaveCount(2);
    }

    [Test]
    public void Variance_StoresValue()
    {
        var opts = new ScheduleOptions().Variance(TimeSpan.FromSeconds(30));
        opts.ToManifestOptions().Variance.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void Group_String_AppliesGroupId()
    {
        var opts = new ScheduleOptions().Group("g1");
        opts._groupId.Should().Be("g1");
    }

    [Test]
    public void Group_StringWithConfigure_BuildsGroupOptions()
    {
        var opts = new ScheduleOptions().Group("g1", g => g.MaxActiveJobs(5));
        opts._groupId.Should().Be("g1");
        opts._groupOptions.Should().NotBeNull();
    }

    [Test]
    public void Group_ConfigureOnly_BuildsGroupOptionsWithoutGroupId()
    {
        var opts = new ScheduleOptions().Group(g => g.MaxActiveJobs(5));
        opts._groupOptions.Should().NotBeNull();
        opts._groupId.Should().BeNull();
    }

    [Test]
    public void PrunePrefix_StoresPrefix()
    {
        var opts = new ScheduleOptions().PrunePrefix("my-prefix");
        opts._prunePrefix.Should().Be("my-prefix");
    }

    [Test]
    public void ChainedFluentSetters_AllAppliedToManifestOptions()
    {
        var manifestOpts = new ScheduleOptions()
            .Priority(5)
            .Enabled(true)
            .MaxRetries(2)
            .Timeout(TimeSpan.FromSeconds(30))
            .Variance(TimeSpan.FromSeconds(5))
            .ToManifestOptions();

        manifestOpts.Priority.Should().Be(5);
        manifestOpts.IsEnabled.Should().BeTrue();
        manifestOpts.MaxRetries.Should().Be(2);
        manifestOpts.Timeout.Should().Be(TimeSpan.FromSeconds(30));
        manifestOpts.Variance.Should().Be(TimeSpan.FromSeconds(5));
    }
}
