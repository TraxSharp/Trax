using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Every host start schedules its manifests again. A re-seed writes only the fields the code
/// states; a field it leaves unstated keeps whatever an operator set at runtime.
///
/// <para>Enforces <c>docs/adr/0011-a-re-seed-writes-only-the-settings-the-code-states.md</c>: code wins only for the settings it states.</para>
/// </summary>
[Property("adr", "docs/adr/0011-a-re-seed-writes-only-the-settings-the-code-states.md")]
[TestFixture]
public class SeedingPreservesOperatorStateTests
{
    [Test]
    public async Task A_manifest_disabled_at_runtime_stays_disabled_when_it_is_seeded_again()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        await ScheduleAsync(fx, "kill-switch");
        await fx.Scheduler.DisableAsync("kill-switch");

        await ScheduleAsync(fx, "kill-switch");

        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == "kill-switch");
        manifest
            .IsEnabled.Should()
            .BeFalse(
                "the code does not state Enabled, so the operator's switch stands. See docs/adr/0011-a-re-seed-writes-only-the-settings-the-code-states.md."
            );
    }

    [Test]
    public async Task A_manifest_whose_code_states_Enabled_takes_the_code_value_on_a_re_seed()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        await ScheduleAsync(fx, "stated-enabled", o => o.Enabled(true));
        await fx.Scheduler.DisableAsync("stated-enabled");

        await ScheduleAsync(fx, "stated-enabled", o => o.Enabled(true));

        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == "stated-enabled");
        manifest
            .IsEnabled.Should()
            .BeTrue(
                "the code states Enabled(true), and code wins. See docs/adr/0011-a-re-seed-writes-only-the-settings-the-code-states.md."
            );
    }

    [Test]
    public async Task Runtime_group_edits_survive_a_re_seed_that_states_no_group_settings()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        var manifest = await ScheduleAsync(fx, "grouped", o => o.Group("ops-edited"));
        var ops = fx.Services.GetRequiredService<IOperationsService>();
        var result = await ops.UpdateManifestGroupAsync(
            manifest.ManifestGroupId,
            new UpdateManifestGroupInput(MaxActiveJobs: 5, Priority: 20, IsEnabled: false),
            CancellationToken.None
        );
        result.Success.Should().BeTrue();

        await ScheduleAsync(fx, "grouped", o => o.Group("ops-edited"));

        var group = await fx
            .DataContext.ManifestGroups.AsNoTracking()
            .FirstAsync(g => g.Name == "ops-edited");
        const string because =
            "the code states no group settings, so runtime edits stand. See docs/adr/0011-a-re-seed-writes-only-the-settings-the-code-states.md.";
        group.MaxActiveJobs.Should().Be(5, because);
        group.Priority.Should().Be(20, because);
        group.IsEnabled.Should().BeFalse(because);
    }

    [Test]
    public async Task Group_settings_the_code_states_are_written_on_a_re_seed()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        var manifest = await ScheduleAsync(
            fx,
            "grouped-stated",
            o => o.Group("code-owned", g => g.MaxActiveJobs(2))
        );
        var ops = fx.Services.GetRequiredService<IOperationsService>();
        await ops.UpdateManifestGroupAsync(
            manifest.ManifestGroupId,
            new UpdateManifestGroupInput(MaxActiveJobs: 9, Priority: 20),
            CancellationToken.None
        );

        await ScheduleAsync(
            fx,
            "grouped-stated",
            o => o.Group("code-owned", g => g.MaxActiveJobs(2))
        );

        var group = await fx
            .DataContext.ManifestGroups.AsNoTracking()
            .FirstAsync(g => g.Name == "code-owned");
        group
            .MaxActiveJobs.Should()
            .Be(
                2,
                "the code states MaxActiveJobs(2). See docs/adr/0011-a-re-seed-writes-only-the-settings-the-code-states.md."
            );
        group
            .Priority.Should()
            .Be(
                20,
                "the code does not state the group priority. See docs/adr/0011-a-re-seed-writes-only-the-settings-the-code-states.md."
            );
    }

    [Test]
    public async Task A_stated_failure_window_is_written_and_an_unstated_one_keeps_the_stored_window()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        var created = await ScheduleAsync(
            fx,
            "windowed",
            o => o.FailureWindow(TimeSpan.FromHours(2))
        );
        created.FailureWindowSeconds.Should().Be(7200);

        await ScheduleAsync(fx, "windowed", o => o.FailureWindow(TimeSpan.FromHours(6)));
        (await LoadAsync(fx, "windowed"))
            .FailureWindowSeconds.Should()
            .Be(21600, "the code states a new window, and code wins");

        await ScheduleAsync(fx, "windowed");
        (await LoadAsync(fx, "windowed"))
            .FailureWindowSeconds.Should()
            .Be(
                21600,
                "the code no longer states a window, so the stored one stands. See docs/adr/0011-a-re-seed-writes-only-the-settings-the-code-states.md."
            );

        (await ScheduleAsync(fx, "unwindowed"))
            .FailureWindowSeconds.Should()
            .BeNull("a manifest that states no window uses the scheduler's");
    }

    private static Task<Effect.Models.Manifest.Manifest> LoadAsync(
        SchedulerE2EFixture fx,
        string externalId
    ) => fx.DataContext.Manifests.AsNoTracking().FirstAsync(m => m.ExternalId == externalId);

    [Test]
    public async Task Runtime_edits_to_retries_timeout_and_priority_survive_a_re_seed_that_states_none()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });

        await ScheduleAsync(fx, "ops-tuned");
        await EditAsync(fx, "ops-tuned", maxRetries: 9, timeoutSeconds: 600, priority: 17);

        await ScheduleAsync(fx, "ops-tuned");

        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == "ops-tuned");
        const string because =
            "the code states none of them, so the operator's values stand. See docs/adr/0011-a-re-seed-writes-only-the-settings-the-code-states.md.";
        manifest.MaxRetries.Should().Be(9, because);
        manifest.TimeoutSeconds.Should().Be(600, because);
        manifest.Priority.Should().Be(17, because);
    }

    [Test]
    public async Task Retries_timeout_and_priority_the_code_states_are_written_on_a_re_seed()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });
        Action<Configuration.ScheduleOptions> stated = o =>
            o.MaxRetries(2).Timeout(TimeSpan.FromMinutes(1)).Priority(4);

        await ScheduleAsync(fx, "code-tuned", stated);
        await EditAsync(fx, "code-tuned", maxRetries: 9, timeoutSeconds: 600, priority: 17);

        await ScheduleAsync(fx, "code-tuned", stated);

        var manifest = await fx
            .DataContext.Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == "code-tuned");
        const string because =
            "the code states each of them, and code wins. See docs/adr/0011-a-re-seed-writes-only-the-settings-the-code-states.md.";
        manifest.MaxRetries.Should().Be(2, because);
        manifest.TimeoutSeconds.Should().Be(60, because);
        manifest.Priority.Should().Be(4, because);
    }

    [Test]
    public async Task A_batch_item_writes_only_what_its_configure_each_states_on_a_re_seed()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(_ => { });
        Task Seed() =>
            fx.Scheduler.ScheduleManyAsync<ISchedulerTestTrain, SchedulerTestInput, Unit, string>(
                ["batch-a", "batch-b"],
                id => (id, new SchedulerTestInput { Value = id }),
                Every.Minutes(5),
                configureEach: (id, o) =>
                {
                    if (id == "batch-a")
                        o.MaxRetries = 1;
                }
            );

        await Seed();
        await EditAsync(fx, "batch-a", maxRetries: 9, timeoutSeconds: 600, priority: 17);
        await EditAsync(fx, "batch-b", maxRetries: 9, timeoutSeconds: 600, priority: 17);

        await Seed();

        var manifests = await fx
            .DataContext.Manifests.AsNoTracking()
            .Where(m => m.ExternalId == "batch-a" || m.ExternalId == "batch-b")
            .ToDictionaryAsync(m => m.ExternalId);
        manifests["batch-a"].MaxRetries.Should().Be(1, "configureEach states it for batch-a");
        manifests["batch-a"].Priority.Should().Be(17, "nothing states batch-a's priority");
        manifests["batch-b"].MaxRetries.Should().Be(9, "nothing states batch-b's retries");
        manifests["batch-b"].TimeoutSeconds.Should().Be(600);
    }

    /// <summary>An operator's runtime edit, as an update-manifest action writes it.</summary>
    private static async Task EditAsync(
        SchedulerE2EFixture fx,
        string externalId,
        int maxRetries,
        int timeoutSeconds,
        int priority
    )
    {
        await fx
            .DataContext.Manifests.Where(m => m.ExternalId == externalId)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(m => m.MaxRetries, maxRetries)
                    .SetProperty(m => m.TimeoutSeconds, timeoutSeconds)
                    .SetProperty(m => m.Priority, priority)
            );
        fx.DataContext.Reset();
    }

    private static Task<Effect.Models.Manifest.Manifest> ScheduleAsync(
        SchedulerE2EFixture fx,
        string externalId,
        Action<Configuration.ScheduleOptions>? options = null
    ) =>
        fx.Scheduler.ScheduleAsync<ISchedulerTestTrain, SchedulerTestInput, Unit>(
            externalId,
            new SchedulerTestInput { Value = externalId },
            Every.Minutes(5),
            options
        );
}
