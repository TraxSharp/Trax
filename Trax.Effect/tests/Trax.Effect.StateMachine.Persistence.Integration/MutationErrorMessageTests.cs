using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

public enum ProbeState
{
    Open,
    Closed,
}

public enum ProbeTrigger
{
    Close,
}

/// <summary>A machine whose only guard throws, carrying text that must stay on the server.</summary>
public sealed class ThrowingGuardMachine : Machine<ProbeState, ProbeTrigger>
{
    public const string Secret = "Host=db.internal;Password=hunter2";

    protected override void Configure(IMachineBuilder<ProbeState, ProbeTrigger> m)
    {
        m.Id("throwing-guard").Version(1).StartsAt(ProbeState.Open, () => new JsonObject());
        m.In(ProbeState.Open)
            .On(ProbeTrigger.Close)
            .When((_, _) => throw new InvalidOperationException(Secret))
            .To(ProbeState.Closed);
    }
}

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
        (
            await new SaveSnapshotJunction(registry, User).Run(
                new SaveSnapshotInput
                {
                    Machine = "order",
                    Id = id,
                    Snapshot = OrderMachine.ReviewSnapshot(1),
                }
            )
        ).Problem.Should().BeNull();
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

        send.Problem!.Code.Should().Be("delivery-failed");
        send.Problem.Message.Should().NotContain(Secret);
        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.Exception!.Message.Should().Be(Secret);
        send.Problem.Message.Should().Contain(entry.Reference);
    }

    [Test]
    public async Task A_cancelled_send_is_not_reported_as_a_failed_delivery()
    {
        using var request = new CancellationTokenSource();
        var registry = Registry(new CancellingCharge(request));
        var id = await SeedReview(registry);

        var act = () =>
            new CancellableSend(registry, request.Token).Run(
                new SendSnapshotInput { Machine = "order", Id = id }
            );

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>The send mutation as a request whose token the test controls.</summary>
    private sealed class CancellableSend : SendSnapshotJunction
    {
        public CancellableSend(ISnapshotMachineRegistry registry, CancellationToken token)
            : base(registry, User) => CancellationToken = token;
    }

    /// <summary>A charge during which the request goes away, and which then reports the cancellation.</summary>
    private sealed class CancellingCharge(CancellationTokenSource request) : IOrderCharge
    {
        public async Task<string> Run(
            Snapshot snapshot,
            CancellationToken cancellationToken = default
        )
        {
            await request.CancelAsync();
            throw new OperationCanceledException(request.Token);
        }
    }

    [Test]
    public async Task Advance_failure_must_not_return_the_raw_exception_message()
    {
        var registry = Registry(new CountingEffect());
        var id = Guid.NewGuid();
        (
            await new SaveSnapshotJunction(registry, User).Run(
                new SaveSnapshotInput
                {
                    Machine = "throwing-guard",
                    Id = id,
                    Snapshot =
                        "{\"machine\":\"throwing-guard\",\"version\":1,\"state\":\"Open\",\"context\":{}}",
                }
            )
        ).Problem.Should().BeNull();
        var logger = new CapturingLogger<AdvanceSnapshotJunction>();

        var advance = await new AdvanceSnapshotJunction(registry, User, logger).Run(
            new AdvanceSnapshotInput
            {
                Machine = "throwing-guard",
                Id = id,
                Trigger = "Close",
            }
        );

        advance.Problem!.Code.Should().Be("internal-error");
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
                    ?.FirstOrDefault(p => p.Key == "Reference")
                    .Value?.ToString()
                ?? "";
            Entries.Add(new Entry(exception, reference));
        }
    }
}
