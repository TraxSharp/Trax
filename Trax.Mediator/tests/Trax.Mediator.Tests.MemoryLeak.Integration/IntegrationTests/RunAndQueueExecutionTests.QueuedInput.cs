using System.Text;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.IntegrationTests;

public partial class RunAndQueueExecutionTests
{
    [Test]
    public async Task QueueAsync_StoredInputIsNotLargerThanTheInputCapAllows()
    {
        // One shared row, referenced many times through JSON reference metadata. The caller's
        // JSON is far under the 256 KiB cap; an enqueue that honoured the references would write
        // every one of them out in full when it stores the input.
        var row = string.Join(",", Enumerable.Repeat("\"x\"", 500));
        var refs = string.Join(",", Enumerable.Repeat("""{"$ref":"2"}""", 2_000));
        var inputJson =
            """{"rows":{"$id":"1","$values":[{"$id":"2","$values":[""" + row + "]}," + refs + "]}}";

        Encoding.UTF8.GetByteCount(inputJson).Should().BeLessThan(64 * 1024);

        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        QueueTrainResult? queued = null;

        try
        {
            queued = await execution.QueueAsync(typeof(IRowsTrain).FullName!, inputJson);
        }
        catch (Exception refused) when (refused is not OutOfMemoryException)
        {
            // Refusing reference metadata in a caller's input is an acceptable answer.
            return;
        }

        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        var stored = await context
            .WorkQueues.AsNoTracking()
            .SingleAsync(w => w.Id == queued.WorkQueueId);

        Encoding
            .UTF8.GetByteCount(stored.Input!)
            .Should()
            .BeLessThanOrEqualTo(
                262_144,
                "MaxInputJsonBytes documents that a queued entry's input is governed by the cap"
            );
    }

    [Test]
    public async Task QueueAsync_ListWrittenWithReferenceMetadata_IsRefusedAsAJsonError()
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var act = () =>
            execution.QueueAsync(
                typeof(IRowsTrain).FullName!,
                """{"rows":{"$id":"1","$values":[]}}"""
            );

        await act.Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    [Test]
    public async Task QueueAsync_ReferenceToAnotherElement_IsNotFollowed()
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var queued = await execution.QueueAsync(
            typeof(IWideTrain).FullName!,
            """{"items":[{"$id":"1","first":5},{"$ref":"1"}]}"""
        );

        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        var stored = await context
            .WorkQueues.AsNoTracking()
            .SingleAsync(w => w.Id == queued.WorkQueueId);

        var items = System
            .Text.Json.JsonDocument.Parse(stored.Input!)
            .RootElement.GetProperty("items");
        items[0].GetProperty("first").GetInt64().Should().Be(5);
        items[1]
            .GetProperty("first")
            .GetInt64()
            .Should()
            .Be(0, "a $ref in a caller's input is an unknown property, not a reference to copy");
    }

    [Test]
    public async Task QueueAsync_InputThatGrowsPastTheStoredCapWhenWritten_IsRefused()
    {
        // Each {} is three bytes of caller JSON and is written back with every member of Wide at
        // its default, so the stored form is many times the size of what the caller sent.
        var elements = string.Join(",", Enumerable.Repeat("{}", 20_000));
        var inputJson = """{"items":[""" + elements + "]}";
        Encoding.UTF8.GetByteCount(inputJson).Should().BeLessThan(262_144);

        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var act = () => execution.QueueAsync(typeof(IWideTrain).FullName!, inputJson);

        var refused = (await act.Should().ThrowAsync<TrainInputValidationException>()).Which;
        refused.MaxBytes.Should().Be(TrainInputReader.StoredInputGrowthFactor * 262_144);
        refused.ObservedBytes.Should().BeGreaterThan(refused.MaxBytes);
    }

    [Test]
    public async Task QueueAsync_InputFarPastTheStoredCap_IsRefusedWithoutWritingItAllOut()
    {
        // 80,000 empty objects: under the caller cap as sent, and some 20 MB once each is written
        // indented with all eight members. As a string that is twice as much again.
        var elements = string.Join(",", Enumerable.Repeat("{}", 80_000));
        var inputJson = """{"items":[""" + elements + "]}";
        Encoding.UTF8.GetByteCount(inputJson).Should().BeLessThan(262_144);

        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var act = () => execution.QueueAsync(typeof(IWideTrain).FullName!, inputJson);
        await act.Should().ThrowAsync<TrainInputValidationException>();
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        allocated
            .Should()
            .BeLessThan(
                32L * 1024 * 1024,
                "the stored form is refused once it passes the 1 MiB cap, so the refusal must "
                    + "not cost the tens of megabytes writing it all out would"
            );
    }

    [Test]
    public async Task QueueAsync_OrdinaryInput_IsStoredAsBefore()
    {
        using var scope = _serviceProvider.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

        var queued = await execution.QueueAsync(
            typeof(IRowsTrain).FullName!,
            """{"rows":[["a","b"],["c"]]}"""
        );

        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        var stored = await context
            .WorkQueues.AsNoTracking()
            .SingleAsync(w => w.Id == queued.WorkQueueId);

        stored.Input.Should().Contain("\"a\"").And.Contain("\"c\"");
    }

    public record RowsInput
    {
        public List<List<string>> Rows { get; init; } = [];
    }

    public interface IRowsTrain : IServiceTrain<RowsInput, Unit>;

    public class RowsTrain : ServiceTrain<RowsInput, Unit>, IRowsTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }

    public record Wide
    {
        public long First { get; init; }
        public long Second { get; init; }
        public long Third { get; init; }
        public long Fourth { get; init; }
        public long Fifth { get; init; }
        public long Sixth { get; init; }
        public long Seventh { get; init; }
        public long Eighth { get; init; }
        public long Ninth { get; init; }
        public long Tenth { get; init; }
    }

    public record WideInput
    {
        public List<Wide> Items { get; init; } = [];
    }

    public interface IWideTrain : IServiceTrain<WideInput, Unit>;

    public class WideTrain : ServiceTrain<WideInput, Unit>, IWideTrain
    {
        protected override Task<Either<Exception, Unit>> Junctions() =>
            Task.FromResult<Either<Exception, Unit>>(Unit.Default);
    }
}
