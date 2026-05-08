using FluentAssertions;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

[TestFixture]
public class NoOpBroadcasterTests
{
    [Test]
    public async Task PublishAsync_ReturnsCompletedTask_WithoutAttemptingTransport()
    {
        // The no-op broadcaster is the default registration. The contract is
        // that PublishAsync immediately returns a completed Task — never
        // touches a network and never throws — so the storage layer's
        // PublishAsync wrapper is a zero-cost call when no real broadcaster
        // is configured.
        var b = new NoOpPersistedOperationBroadcaster();
        var task = b.PublishAsync(
            new PersistedOperationChangedMessage(
                null,
                "id1",
                PersistedOperationChangeType.Upsert,
                DateTime.UtcNow
            ),
            CancellationToken.None
        );

        task.IsCompleted.Should().BeTrue("the no-op publish must complete synchronously");
        task.IsFaulted.Should().BeFalse();
        await task;
    }

    [Test]
    public async Task PublishAsync_NullMessage_StillCompletes()
    {
        // Null message is technically invalid input, but the no-op broadcaster
        // never inspects the message — it must not throw on any input so
        // that storage's broadcaster-error suppression is never invoked for
        // the default path.
        var b = new NoOpPersistedOperationBroadcaster();
        var task = b.PublishAsync(null!, CancellationToken.None);
        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    [Test]
    public async Task PublishAsync_CancelledToken_StillCompletesWithoutCancellation()
    {
        // The no-op contract: never observe the cancellation token. Storage's
        // broadcaster-error path expects this so that publish failures are
        // never user-visible.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var b = new NoOpPersistedOperationBroadcaster();
        var task = b.PublishAsync(
            new PersistedOperationChangedMessage(
                null,
                "id",
                PersistedOperationChangeType.Upsert,
                DateTime.UtcNow
            ),
            cts.Token
        );
        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    [Test]
    public void ChangeTypeConstants_AreNonEmpty()
    {
        PersistedOperationChangeType.Upsert.Should().Be("Upsert");
        PersistedOperationChangeType.Deactivate.Should().Be("Deactivate");
        PersistedOperationChangeType.Restore.Should().Be("Restore");
    }
}
