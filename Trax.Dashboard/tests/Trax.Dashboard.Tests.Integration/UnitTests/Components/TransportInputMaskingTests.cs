using Bunit;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Pages.Data;
using Trax.Dashboard.Services.DashboardSettings;
using Trax.Dashboard.Services.LocalStorage;
using Trax.Dashboard.Tests.Integration.Fakes.Data;
using Trax.Dashboard.Tests.Integration.Fakes.Services;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

/// <summary>
/// A work queue entry's input and a manifest's properties are the copies Trax keeps unmasked,
/// because a run reads its input from them. The dashboard shows them the way it shows a run's
/// recorded input: each <c>[TraxSensitive]</c> member as <c>{"_redacted": true}</c>, and the
/// whole value masked when it cannot be read as a registered train input, as the API does for
/// the same reads.
/// </summary>
[TestFixture]
public class TransportInputMaskingTests
{
    private const string Secret = "hunter2-do-not-show";

    private Bunit.TestContext _ctx = null!;
    private InMemoryDataContextFactory _data = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _data = new InMemoryDataContextFactory();

        var trains = new ServiceCollection();
        trains.AddScopedTraxRoute<ISensitiveTrain, SensitiveTrain>();

        var services = _ctx.Services;
        services.AddSingleton<IDataContextProviderFactory>(_data);
        services.AddSingleton<ILocalStorageService, InMemoryLocalStorageService>();
        services.AddSingleton<IDashboardSettingsService, DashboardSettingsService>();
        services.AddSingleton<ITrainDiscoveryService>(new TrainDiscoveryService(trains));
        services.AddSingleton(UnusedService<IOperationsService>.Create());
        services.AddSingleton(UnusedService<ITraxScheduler>.Create());
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    [Test]
    public async Task A_work_queue_entry_shows_its_sensitive_input_member_masked()
    {
        var id = await SeedEntryAsync(typeof(SensitiveInput).FullName);

        var page = _ctx.RenderComponent<WorkQueueDetailPage>(p => p.Add(x => x.WorkQueueId, id));

        var input = page.WaitForElement("pre", TimeSpan.FromSeconds(10)).TextContent;
        input.Should().Contain("visible-name", "a member that is not sensitive is shown");
        input.Should().Contain("\"_redacted\": true", "the sensitive member is shown masked");
        page.Markup.Should().NotContain(Secret, "a [TraxSensitive] value is never shown");
    }

    [Test]
    public async Task A_work_queue_entry_whose_input_type_is_not_registered_is_masked_whole()
    {
        var id = await SeedEntryAsync("Acme.Unregistered.Input");

        var page = _ctx.RenderComponent<WorkQueueDetailPage>(p => p.Add(x => x.WorkQueueId, id));

        var input = page.WaitForElement("pre", TimeSpan.FromSeconds(10)).TextContent;
        input.Should().Contain("\"_redacted\": true");
        page.Markup.Should()
            .NotContain(Secret, "nothing proves an unreadable input holds no sensitive member")
            .And.NotContain("visible-name");
    }

    [Test]
    public async Task A_dead_letters_manifest_properties_show_the_sensitive_member_masked()
    {
        var id = await SeedDeadLetterAsync();

        var page = _ctx.RenderComponent<DeadLetterDetailPage>(p => p.Add(x => x.DeadLetterId, id));

        page.WaitForAssertion(
            () => page.Markup.Should().Contain("visible-name"),
            TimeSpan.FromSeconds(10)
        );
        page.FindAll("pre")
            .Select(p => p.TextContent)
            .Should()
            .Contain(t => t.Contains("visible-name") && t.Contains("\"_redacted\": true"));
        page.Markup.Should().NotContain(Secret, "a [TraxSensitive] value is never shown");
    }

    // As the enqueue stores it: camel-cased, the way the dispatcher reads it back.
    private static string SensitiveJson() =>
        $$"""{"name":"visible-name","password":"{{Secret}}"}""";

    private async Task<long> SeedEntryAsync(string? inputTypeName)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(ISensitiveTrain).FullName!,
                Input = SensitiveJson(),
                InputTypeName = inputTypeName,
            }
        );

        await using var db = await _data.CreateDbContextAsync(default);
        await db.Track(entry);
        await db.SaveChanges(default);
        return entry.Id;
    }

    private async Task<long> SeedDeadLetterAsync()
    {
        await using var db = await _data.CreateDbContextAsync(default);
        var group = new ManifestGroup { Name = "sensitive" };
        await db.Track(group);
        await db.SaveChanges(default);

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(ISensitiveTrain),
                Properties = new SensitiveInput { Name = "visible-name", Password = Secret },
            }
        );
        manifest.ManifestGroupId = group.Id;
        await db.Track(manifest);
        await db.SaveChanges(default);
        manifest.Properties.Should().Contain(Secret, "the premise is a stored copy in clear");

        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "retries exhausted",
                RetryCount = 3,
            }
        );
        await db.Track(deadLetter);
        await db.SaveChanges(default);
        return deadLetter.Id;
    }

    public record SensitiveInput : IManifestProperties
    {
        public string Name { get; init; } = "";

        [TraxSensitive]
        public string Password { get; init; } = "";
    }

    public interface ISensitiveTrain : IServiceTrain<SensitiveInput, Unit> { }

    public class SensitiveTrain : ServiceTrain<SensitiveInput, Unit>, ISensitiveTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}
