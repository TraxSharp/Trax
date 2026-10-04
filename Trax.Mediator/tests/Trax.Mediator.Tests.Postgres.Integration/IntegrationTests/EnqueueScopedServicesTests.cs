using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Tests.Postgres.Integration.Fixtures;

namespace Trax.Mediator.Tests.Postgres.Integration.IntegrationTests;

/// <summary>
/// The scoped services an <c>OnQueue</c> hook takes belong to that enqueue, not to the caller.
/// A caller scope can live as long as a Blazor circuit, so a hook that left a failed write in a
/// scoped <c>DbContext</c> used to fail every later enqueue of the train from that tab.
///
/// <para>Enforces <c>docs/adr/0002-an-enqueue-resolves-its-train-in-a-scope-of-its-own.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0002-an-enqueue-resolves-its-train-in-a-scope-of-its-own.md")]
public class EnqueueScopedServicesTests : TestSetup
{
    private ITrainExecutionService Execution =>
        Scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();

    [SetUp]
    public async Task ClearAsync()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        await context
            .WorkQueues.Where(w => w.TrainName!.StartsWith("EnqueueScope."))
            .ExecuteDeleteAsync();
    }

    [Test]
    public async Task A_hook_whose_scoped_write_failed_does_not_fail_the_next_enqueue_from_that_scope()
    {
        var poisoned = async () =>
            await Execution.QueueAsync(
                typeof(IScopedWriterTrain).FullName!,
                """{"poison":true,"label":"first"}"""
            );

        await poisoned
            .Should()
            .ThrowAsync<DbUpdateException>("the hook's write breaks a unique index");

        var clean = async () =>
            await Execution.QueueAsync(
                typeof(IScopedWriterTrain).FullName!,
                """{"poison":false,"label":"second"}"""
            );

        await clean
            .Should()
            .NotThrowAsync(
                "the failed write belonged to the first enqueue's DbContext, and a second "
                    + "enqueue from the same caller scope gets a DbContext of its own (see "
                    + "docs/adr/0002-an-enqueue-resolves-its-train-in-a-scope-of-its-own.md)"
            );

        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = await factory.CreateDbContextAsync(CancellationToken.None);
        (await context.WorkQueues.CountAsync(w => w.TrainName == "EnqueueScope.Marker.second"))
            .Should()
            .Be(1, "the second hook's own write landed");
    }

    public record ScopedWriterInput
    {
        public bool Poison { get; init; }
        public string Label { get; init; } = "default";
    }

    public interface IScopedWriterTrain : IServiceTrain<ScopedWriterInput, Unit>;

    /// <summary>
    /// Writes through the scoped <see cref="IDataContext"/>, the way a consumer's hook writes
    /// through its own scoped <c>DbContext</c>.
    /// </summary>
    public class ScopedWriterTrain(IDataContext scoped)
        : ServiceTrain<ScopedWriterInput, Unit>,
            IScopedWriterTrain
    {
        protected override async Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            var input = metadata.GetInput<ScopedWriterInput>()!;

            if (input.Poison)
            {
                // Two rows sharing an ExternalId: the unique index refuses the pair.
                var first = Row("EnqueueScope.Duplicate");
                var second = Row("EnqueueScope.Duplicate");
                second.ExternalId = first.ExternalId;
                await scoped.Track(first);
                await scoped.Track(second);
            }

            await scoped.Track(Row($"EnqueueScope.Marker.{input.Label}"));
            await scoped.SaveChanges(ct);
        }

        protected override Task<Either<Exception, Unit>> Junctions() => Task.FromResult(Resolve());

        private static WorkQueue Row(string name) =>
            WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = name,
                    Input = "{}",
                    InputTypeName = typeof(ScopedWriterInput).FullName,
                }
            );
    }
}
