using AwesomeAssertions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

/// <summary>
/// <c>QueueSubjectKey</c> and <c>OnQueue</c> are called on an instance that has not run. The
/// enqueue path hands them the input through <see cref="ServiceTrain{TIn,TOut}.EnterQueueHooks"/>,
/// so <c>TrainInput</c> reads it there instead of silently returning default, without giving the
/// instance a <see cref="ServiceTrain{TIn,TOut}.Metadata"/> a later run would mistake for its own.
///
/// <para>Enforces Trax.Docs/adr/0021-a-queue-hook-reads-its-input-through-traininput.md.</para>
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0021-a-queue-hook-reads-its-input-through-traininput.md")]
public class QueueHookInputTests
{
    private static Metadata Queued(object input) =>
        Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(OrderTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = input,
            }
        );

    [Test]
    public void QueueSubjectKey_reads_the_input_through_TrainInput()
    {
        var train = new OrderTrain();
        var metadata = Queued(new Order(42));

        using (train.EnterQueueHooks(metadata))
            train
                .SubjectKeyFor(metadata)
                .Should()
                .Be(
                    "order:42",
                    "a key built from TrainInput read order 0 for every enqueue, so every order "
                        + "serialized behind every other"
                );
    }

    [Test]
    public async Task OnQueue_reads_the_input_through_TrainInput()
    {
        var train = new OrderTrain();
        var metadata = Queued(new Order(7));

        using (train.EnterQueueHooks(metadata))
            await train.OnQueueFor(metadata);

        train.QueuedOrderId.Should().Be(7);
    }

    [Test]
    public void Leaving_the_scope_leaves_the_instance_as_it_was()
    {
        var train = new OrderTrain();

        using (train.EnterQueueHooks(Queued(new Order(42)))) { }

        train.Input.Should().BeNull("the input belongs to the enqueue, not to the instance");
        train.Metadata.Should().BeNull("a later run must initialize its own metadata");
    }

    [Test]
    public void Disposing_the_scope_twice_is_harmless()
    {
        var train = new OrderTrain();
        var scope = train.EnterQueueHooks(Queued(new Order(42)));

        scope.Dispose();
        var act = () => scope.Dispose();

        act.Should().NotThrow();
        train.Input.Should().BeNull();
    }

    [Test]
    public void An_inner_scope_on_the_same_instance_shadows_and_restores_the_outer_one()
    {
        var train = new OrderTrain();

        using (train.EnterQueueHooks(Queued(new Order(1))))
        {
            using (train.EnterQueueHooks(Queued(new Order(2))))
                train.Input!.Id.Should().Be(2);

            train.Input!.Id.Should().Be(1);
        }
    }

    [Test]
    public void Another_instance_does_not_see_the_input()
    {
        var entered = new OrderTrain();
        var other = new OrderTrain();

        using (entered.EnterQueueHooks(Queued(new Order(42))))
            other.Input.Should().BeNull("the input is scoped to the instance it was handed to");
    }

    [Test]
    public async Task Concurrent_enqueues_on_one_instance_each_read_their_own_input()
    {
        // A scoped train resolved twice in one scope is one instance, and a scope shared by
        // concurrent enqueues (a Blazor circuit) runs both hooks on it at once.
        var train = new OrderTrain();
        var bothEntered = new TaskCompletionSource();
        var entered = 0;

        async Task<int?> Enqueue(int orderId)
        {
            using (train.EnterQueueHooks(Queued(new Order(orderId))))
            {
                if (Interlocked.Increment(ref entered) == 2)
                    bothEntered.SetResult();

                await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return train.Input?.Id;
            }
        }

        var seen = await Task.WhenAll(Task.Run(() => Enqueue(1)), Task.Run(() => Enqueue(2)));

        seen.Should().Equal(1, 2);
    }

    [Test]
    public void Entering_a_train_that_already_has_metadata_is_refused()
    {
        var train = new OrderTrain();
        typeof(ServiceTrain<Order, string>)
            .GetProperty(nameof(OrderTrain.Metadata))!
            .SetValue(train, Queued(new Order(1)));

        var act = () => train.EnterQueueHooks(Queued(new Order(2)));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*already has metadata*",
                "an instance that has run reads its own input, and must not be handed another"
            );
    }

    [Test]
    public void Entering_with_an_input_of_another_type_is_refused()
    {
        var train = new OrderTrain();

        var act = () => train.EnterQueueHooks(Queued("not an order"));

        act.Should().Throw<ArgumentException>().WithMessage($"*{nameof(Order)}*");
    }

    [Test]
    public void TrainInput_outside_a_queue_hook_on_a_train_that_has_not_run_is_still_default()
    {
        new OrderTrain().Input.Should().BeNull();
    }

    public record Order(int Id);

    private class OrderTrain : ServiceTrain<Order, string>
    {
        public int? QueuedOrderId { get; private set; }

        internal Order? Input => TrainInput;

        internal string? SubjectKeyFor(Metadata metadata) => QueueSubjectKey(metadata);

        internal Task OnQueueFor(Metadata metadata) => OnQueue(metadata, CancellationToken.None);

        protected override string? QueueSubjectKey(Metadata metadata) => $"order:{TrainInput.Id}";

        protected override Task OnQueue(Metadata metadata, CancellationToken ct)
        {
            QueuedOrderId = TrainInput.Id;
            return Task.CompletedTask;
        }

        protected override Task<Trax.Core.Functional.Either<Exception, string>> Junctions() =>
            Task.FromResult<Trax.Core.Functional.Either<Exception, string>>("done");
    }
}
