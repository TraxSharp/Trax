using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// ScheduleManyAsync gives each item a copy of the batch's options, which configureEach may
/// then change for that item alone.
/// </summary>
[TestFixture]
public class ScheduleManyItemOptionsTests
{
    [Test]
    public async Task ScheduleMany_keeps_the_batch_misfire_policy_and_threshold_on_every_item()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        await fx.Scheduler.ScheduleManyAsync<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
            ["a", "b"],
            id => ($"misfire-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5),
            options =>
                options.OnMisfire(MisfirePolicy.DoNothing).MisfireThreshold(TimeSpan.FromMinutes(5))
        );

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Where(m => m.ExternalId.StartsWith("misfire-"))
            .ToListAsync();

        manifests.Should().HaveCount(2);
        manifests.Should().AllSatisfy(m => m.MisfirePolicy.Should().Be(MisfirePolicy.DoNothing));
        manifests.Should().AllSatisfy(m => m.MisfireThresholdSeconds.Should().Be(300));
    }

    [Test]
    public async Task ScheduleMany_configureEach_exclusion_applies_to_its_own_item_only()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        await fx.Scheduler.ScheduleManyAsync<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
            ["weekday-only", "every-day"],
            id => ($"excl-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5),
            configureEach: (id, item) =>
            {
                if (id == "weekday-only")
                    item.Exclusions.Add(Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday));
            }
        );

        var everyDay = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == "excl-every-day");

        everyDay
            .GetExclusions()
            .Should()
            .BeEmpty("only the weekday-only item was given an exclusion");
    }

    [Test]
    public void ManifestOptions_Copy_carries_every_public_property()
    {
        var source = new ManifestOptions
        {
            IsEnabled = false,
            MaxRetries = 9,
            Timeout = TimeSpan.FromMinutes(7),
            Priority = 12,
            IsDormant = true,
            MisfirePolicy = MisfirePolicy.DoNothing,
            MisfireThreshold = TimeSpan.FromMinutes(4),
            Exclusions = [Exclude.DaysOfWeek(DayOfWeek.Sunday)],
            Variance = TimeSpan.FromSeconds(30),
        };

        var copy = source.Copy();

        foreach (var property in typeof(ManifestOptions).GetProperties())
        {
            var original = property.GetValue(source);
            var copied = property.GetValue(copy);
            if (property.Name == nameof(ManifestOptions.Exclusions))
            {
                copied.Should().NotBeSameAs(original, "each item gets its own exclusion list");
                copied.Should().BeEquivalentTo(original);
            }
            else
                copied
                    .Should()
                    .Be(
                        original,
                        $"Copy must carry {property.Name}; a field it misses is lost per item"
                    );
        }
    }
}
