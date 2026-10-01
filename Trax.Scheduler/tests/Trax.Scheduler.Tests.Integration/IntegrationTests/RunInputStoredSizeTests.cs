using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Junction;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>A train whose input holds rows of strings, for the run input size test.</summary>
public interface IRowsRunTrain : IServiceTrain<RowsRunInput, Unit>;

/// <summary>Input for <see cref="IRowsRunTrain"/>.</summary>
public record RowsRunInput : IManifestProperties
{
    public List<List<string>> Rows { get; set; } = [];
}

/// <summary>Does nothing; only its input matters.</summary>
public class RowsRunTrain : ServiceTrain<RowsRunInput, Unit>, IRowsRunTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Task.FromResult<Either<Exception, Unit>>(Unit.Default);
}

/// <summary>A train whose input's stored form is far larger than the JSON a caller sends.</summary>
public interface IPaddedRunTrain : IServiceTrain<PaddedRunInput, Unit>;

/// <summary>
/// Input for <see cref="IPaddedRunTrain"/>. A caller may send <c>{}</c>, but the stored form
/// writes every member, and the default padding is larger than the stored-input cap.
/// </summary>
public record PaddedRunInput : IManifestProperties
{
    public string Padding { get; set; } = new('x', 1_100_000);
}

/// <summary>Does nothing; only its input matters.</summary>
public class PaddedRunTrain : ServiceTrain<PaddedRunInput, Unit>, IPaddedRunTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Task.FromResult<Either<Exception, Unit>>(Unit.Default);
}

/// <summary>
/// A run's input is read the way the mediator reads every caller's input, JSON reference metadata
/// included, and what the run stores for its worker is held to the same stored-input cap a queued
/// input is.
/// </summary>
[TestFixture]
public class RunInputStoredSizeTests
{
    [Test]
    public async Task A_run_input_carrying_json_reference_metadata_is_refused_and_nothing_is_stored()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(
            _ => { },
            services => services.AddScoped<IJobSubmitter, PostgresJobSubmitter>()
        );

        // A single reference: enough to show the metadata is refused, not honoured.
        const string inputJson =
            """{"$id":"1","rows":{"$id":"2","$values":[{"$id":"3","$values":["a"]},{"$ref":"3"}]}}""";

        var ops = fx.Services.GetRequiredService<IOperationsService>();
        var result = await ops.RunTrainAsync(
            new RunTrainInput(typeof(IRowsRunTrain).FullName!, inputJson),
            CancellationToken.None
        );

        result
            .Success.Should()
            .BeFalse(
                "a run's input is exactly the tree the caller wrote; reference metadata is not honoured"
            );
        result.Message.Should().StartWith("Invalid InputJson");
        (await fx.DataContext.BackgroundJobs.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Test]
    public async Task A_run_input_whose_stored_form_is_over_the_stored_cap_is_refused_before_it_is_stored()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(
            _ => { },
            services => services.AddScoped<IJobSubmitter, PostgresJobSubmitter>()
        );

        var ops = fx.Services.GetRequiredService<IOperationsService>();
        var result = await ops.RunTrainAsync(
            new RunTrainInput(typeof(IPaddedRunTrain).FullName!, "{}"),
            CancellationToken.None
        );

        result
            .Success.Should()
            .BeFalse(
                "the stored form is held to StoredInputGrowthFactor times MaxInputJsonBytes, "
                    + "as a queued input is"
            );
        (await fx.DataContext.BackgroundJobs.AsNoTracking().CountAsync()).Should().Be(0);
        (
            await fx
                .DataContext.Metadatas.AsNoTracking()
                .CountAsync(m => m.Name == typeof(IPaddedRunTrain).FullName)
        )
            .Should()
            .Be(0, "a refused run writes no metadata row");
    }

    [Test]
    public async Task A_run_input_within_both_caps_is_stored_for_its_worker()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(
            _ => { },
            services => services.AddScoped<IJobSubmitter, PostgresJobSubmitter>()
        );

        var ops = fx.Services.GetRequiredService<IOperationsService>();
        var result = await ops.RunTrainAsync(
            new RunTrainInput(typeof(IRowsRunTrain).FullName!, """{"rows":[["a","b"],["c"]]}"""),
            CancellationToken.None
        );

        result.Success.Should().BeTrue(result.Message);
        var stored = await fx
            .DataContext.BackgroundJobs.AsNoTracking()
            .Where(j => j.MetadataId == result.Id)
            .Select(j => j.Input)
            .SingleAsync();
        stored.Should().Contain("\"c\"");
    }
}
