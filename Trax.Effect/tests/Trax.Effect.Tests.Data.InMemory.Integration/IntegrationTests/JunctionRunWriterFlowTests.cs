using AwesomeAssertions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Services.EffectProvider;
using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// The junction run writer is often first resolved on a train run's flow, and its loop outlives
/// that run, so the loop must not carry the ambient state of the flow that created it.
/// </summary>
[TestFixture]
public class JunctionRunWriterFlowTests
{
    private static readonly AsyncLocal<string?> Ambient = new();

    [Test]
    public async Task The_write_loop_does_not_carry_the_flow_that_created_the_writer()
    {
        var factory = new ObservingFactory();

        Ambient.Value = "the creating run";
        await using var writer = new JunctionRunWriter(factory);
        Ambient.Value = null;

        writer.Write(
            1,
            new JunctionEventPayload(
                0,
                JunctionRunKind.Junction,
                "J",
                JunctionRunState.InProgress,
                DateTime.UtcNow
            )
        );

        (await factory.Seen.Task.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeNull();
    }

    private sealed class ObservingFactory : IDataContextProviderFactory
    {
        public TaskCompletionSource<string?> Seen { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IEffectProvider Create() => throw new NotSupportedException();

        public Task<IDataContext> CreateDbContextAsync(CancellationToken cancellationToken)
        {
            Seen.TrySetResult(Ambient.Value);
            throw new InvalidOperationException("no database in this test");
        }
    }
}
