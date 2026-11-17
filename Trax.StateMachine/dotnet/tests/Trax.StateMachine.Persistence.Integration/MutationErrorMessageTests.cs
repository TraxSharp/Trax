using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Core.Junction;
using Trax.Effect.Data.Testing;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// What the send and advance mutations tell the client when something fails on the server: a fixed message
/// with a reference, while the exception itself is logged under that reference.
/// </summary>
public class MutationErrorMessageTests
{
    private const string Secret = ThrowingGuardMachine.Secret;
    private static readonly ISnapshotPrincipal User = new FakePrincipal("u");

    private static SnapshotMachineRegistry Registry(IOrderCharge effect) =>
        new(
            new IMachine[] { new OrderMachine(), new ThrowingGuardMachine() },
            TestDb.NewStore(),
            TestDb.NewClaims(),
            new IdempotentEffect(TestDb.NewClaims()),
            new ServiceCollection().AddSingleton(effect).BuildServiceProvider()
        );

    private static async Task<Guid> SeedReview(SnapshotMachineRegistry registry)
    {
        var id = Guid.NewGuid();
        await TestDb
            .Seed(User.CurrentUserKey!, id, OrderMachine.ReviewSnapshot(1))
            .ConfigureAwait(false);
        return id;
    }

    [Test]
    public async Task Send_failure_must_not_return_the_raw_exception_message()
    {
        var registry = Registry(new ThrowingCharge(new InvalidOperationException(Secret)));
        var id = await SeedReview(registry);
        var logger = new CapturingLogger<SendSnapshotJunction>();

        var send = await new SendSnapshotJunction(registry, User, logger).Run(
            new SendSnapshotInput { Machine = "order", Id = id }
        );

        send.Problem!.Code.Should().Be(RefusalCode.DeliveryFailed);
        send.Problem.Message.Should().NotContain(Secret);
        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.Exception!.Message.Should().Be(Secret);
        send.Problem.Message.Should().Contain(entry.Reference);
    }

    [Test]
    [LeavesStuckRuns(
        "a cancelled effect's outcome is unknown, so its claim stays in flight until the lease passes (IdempotentEffect)"
    )]
    public async Task A_cancelled_send_is_not_reported_as_a_failed_delivery()
    {
        using var request = new CancellationTokenSource();
        var registry = Registry(new CancellingCharge(request));
        var id = await SeedReview(registry);

        var send = CancellableSend(registry, request.Token);
        var act = () => send.Run(new SendSnapshotInput { Machine = "order", Id = id });

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// The send mutation as a request whose token the test controls. The junction is sealed and the token's setter
    /// is the train's to call, so the test sets it the way a running train would, through the property.
    /// </summary>
    private static SendSnapshotJunction CancellableSend(
        ISnapshotMachineRegistry registry,
        CancellationToken token
    )
    {
        var send = new SendSnapshotJunction(registry, User);
        typeof(Junction<SendSnapshotInput, SendSnapshotOutput>)
            .GetProperty(nameof(Junction<SendSnapshotInput, SendSnapshotOutput>.CancellationToken))
            .NotNull()
            .SetValue(send, token);
        return send;
    }

    /// <summary>A charge during which the request goes away, and which then reports the cancellation.</summary>
    private sealed class CancellingCharge(CancellationTokenSource request) : IOrderCharge
    {
        public async Task<string> Run(
            Snapshot snapshot,
            CancellationToken cancellationToken = default
        )
        {
            await request.CancelAsync().ConfigureAwait(false);
            throw new OperationCanceledException(request.Token);
        }
    }

    [Test]
    public async Task Advance_failure_must_not_return_the_raw_exception_message()
    {
        var registry = Registry(new CountingEffect());
        var id = Guid.NewGuid();
        await TestDb.Seed(
            "u",
            id,
            "{\"machine\":\"throwing-guard\",\"version\":1,\"state\":\"Open\",\"context\":{}}"
        );
        var logger = new CapturingLogger<AdvanceSnapshotJunction>();

        var advance = await new AdvanceSnapshotJunction(registry, User, logger).Run(
            new AdvanceSnapshotInput
            {
                Machine = "throwing-guard",
                Id = id,
                Trigger = "Close",
            }
        );

        advance.Problem!.Code.Should().Be(RefusalCode.InternalError);
        advance.Problem.Message.Should().NotContain(Secret);
        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.Exception!.Message.Should().Be(Secret);
        advance.Problem.Message.Should().Contain(entry.Reference);
    }

    private sealed class ThrowingCharge(Exception exception) : IOrderCharge
    {
        public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default) =>
            throw exception;
    }

    private sealed record Entry(Exception? Exception, string Reference);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            var reference =
                (state as IEnumerable<KeyValuePair<string, object?>>)
                    ?.FirstOrDefault(p =>
                        string.Equals(p.Key, "Reference", StringComparison.Ordinal)
                    )
                    .Value?.ToString()
                ?? "";
            Entries.Add(new Entry(exception, reference));
        }
    }
}
