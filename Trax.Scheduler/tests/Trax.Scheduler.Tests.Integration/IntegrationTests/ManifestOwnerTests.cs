using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Scheduler.Services.SchedulerStartupService;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Every manifest a scheduler seeds records the application that declared it, and that
/// application's startup prune considers only its own manifests: several applications can
/// schedule against one database without deleting each other's.
///
/// <para>Enforces docs/adr/0015-a-startup-prune-deletes-only-its-own-applications-manifests.md.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0015-a-startup-prune-deletes-only-its-own-applications-manifests.md")]
[NonParallelizable]
public class ManifestOwnerTests
{
    private static Task<SchedulerE2EFixture> BillingHost(string applicationName = "billing") =>
        SchedulerE2EFixture.CreateAsync(
            s =>
                s.RecoverStuckJobsOnStartup(false)
                    .Schedule<ISchedulerTestTrain>(
                        "billing-nightly",
                        new SchedulerTestInput { Value = "b" },
                        Every.Hours(1)
                    ),
            services =>
                services.AddSingleton<IHostEnvironment>(new StubHostEnvironment(applicationName))
        );

    private static Task StartAsync(SchedulerE2EFixture fx) =>
        fx
            .Services.GetServices<IHostedService>()
            .OfType<SchedulerStartupService>()
            .Single()
            .StartAsync(CancellationToken.None);

    [Test]
    public async Task A_seeded_manifest_records_the_application_that_declared_it()
    {
        await using var fx = await BillingHost();

        await StartAsync(fx);

        fx.DataContext.Reset();
        (
            await fx
                .DataContext.Manifests.AsNoTracking()
                .SingleAsync(m => m.ExternalId == "billing-nightly")
        )
            .Owner.Should()
            .Be("billing");
    }

    [Test]
    public async Task The_startup_prune_deletes_only_this_applications_undeclared_manifests()
    {
        await using var fx = await BillingHost();
        await SaveManifestAsync(fx, "billing-removed-from-code", owner: "billing");
        await SaveManifestAsync(fx, "reports-weekly", owner: "reports");
        await SaveManifestAsync(fx, "written-before-owners", owner: null);

        await StartAsync(fx);

        fx.DataContext.Reset();
        var remaining = await fx
            .DataContext.Manifests.AsNoTracking()
            .Select(m => m.ExternalId)
            .ToListAsync();
        remaining.Should().Contain("billing-nightly");
        remaining
            .Should()
            .NotContain("billing-removed-from-code", "billing declared it and no longer does");
        remaining
            .Should()
            .Contain(
                "reports-weekly",
                "another application owns it. See docs/adr/0015-a-startup-prune-deletes-only-its-own-applications-manifests.md."
            );
        remaining
            .Should()
            .Contain(
                "written-before-owners",
                "nothing says which application declared it, so no prune may delete it"
            );
    }

    [Test]
    public async Task A_host_with_no_application_name_prunes_nothing()
    {
        await using var fx = await BillingHost(applicationName: " ");
        await SaveManifestAsync(fx, "someone-elses", owner: "billing");

        await StartAsync(fx);

        fx.DataContext.Reset();
        (await fx.DataContext.Manifests.AnyAsync(m => m.ExternalId == "someone-elses"))
            .Should()
            .BeTrue("a host that cannot name itself cannot tell its manifests from another's");
        (await fx.DataContext.Manifests.SingleAsync(m => m.ExternalId == "billing-nightly"))
            .Owner.Should()
            .BeNull();
    }

    private static async Task SaveManifestAsync(
        SchedulerE2EFixture fx,
        string externalId,
        string? owner
    )
    {
        var group = await TestSetup.CreateAndSaveManifestGroup(
            fx.DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 3600,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = externalId },
                Owner = owner,
            }
        );
        manifest.ExternalId = externalId;
        manifest.ManifestGroupId = group.Id;
        await fx.DataContext.Track(manifest);
        await fx.DataContext.SaveChanges(CancellationToken.None);
        fx.DataContext.Reset();
    }

    private sealed class StubHostEnvironment(string applicationName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = applicationName;

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
