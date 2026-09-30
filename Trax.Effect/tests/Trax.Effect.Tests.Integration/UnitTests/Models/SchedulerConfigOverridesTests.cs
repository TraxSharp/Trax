using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using Trax.Effect.Models.SchedulerConfig;

namespace Trax.Effect.Tests.Integration.UnitTests.Models;

/// <summary>
/// A settings row records which settings a save named, so the rest keep each host's code value.
/// </summary>
[TestFixture]
public class SchedulerConfigOverridesTests
{
    [Test]
    public void A_row_from_before_overrides_names_no_setting()
    {
        var row = new SchedulerConfig();

        row.Overrides.Should().BeNull();
        row.OverriddenSettings.Should().BeEmpty();
        row.TryGetOverride<int?>("MaxActiveJobs", out _).Should().BeFalse();
    }

    [Test]
    public void A_saved_setting_is_named_with_its_value()
    {
        var row = new SchedulerConfig();

        row.SetOverride("StalePendingTimeout", TimeSpan.FromMinutes(5));

        row.OverriddenSettings.Should().Equal("StalePendingTimeout");
        row.TryGetOverride<TimeSpan>("StalePendingTimeout", out var value).Should().BeTrue();
        value.Should().Be(TimeSpan.FromMinutes(5));
        row.TryGetOverride<int?>("MaxActiveJobs", out _)
            .Should()
            .BeFalse("a setting the save did not name keeps the host's code value");
    }

    [Test]
    public void A_null_value_still_counts_as_set()
    {
        var row = new SchedulerConfig();

        row.SetOverride<int?>("MaxActiveJobs", null);

        row.TryGetOverride<int?>("MaxActiveJobs", out var value).Should().BeTrue();
        value.Should().BeNull("null is a real value for MaxActiveJobs: no limit");
    }

    [Test]
    public void Removing_a_setting_returns_it_to_the_code_value()
    {
        var row = new SchedulerConfig();
        row.SetOverride("MaxActiveJobs", 20);
        row.SetOverride("DefaultMaxRetries", 4);

        row.RemoveOverride("MaxActiveJobs").Should().BeTrue();

        row.OverriddenSettings.Should().Equal("DefaultMaxRetries");
        row.RemoveOverride("MaxActiveJobs").Should().BeFalse();
    }

    [Test]
    public void Overrides_that_are_not_an_object_are_refused()
    {
        var row = new SchedulerConfig { Overrides = "[1,2]" };

        Action act = () => row.TryGetOverride<int>("MaxActiveJobs", out _);

        act.Should().Throw<JsonException>();
    }
}
