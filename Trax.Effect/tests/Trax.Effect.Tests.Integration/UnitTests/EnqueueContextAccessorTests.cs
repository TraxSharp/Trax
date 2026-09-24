using FluentAssertions;
using NSubstitute;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.EnqueueContext;

namespace Trax.Effect.Tests.Integration.UnitTests;

/// <summary>
/// Covers the ambient enqueue context an <c>OnQueue</c> hook reads to join the enqueue's
/// transaction. The scoping rules matter: a hook that reads <c>Current</c> outside an enqueue would
/// otherwise silently write on a context nobody commits.
///
/// <para>Enforces Trax.Docs/adr/0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md"
)]
[TestFixture]
public class EnqueueContextAccessorTests
{
    private static IDataContext AContext() => Substitute.For<IDataContext>();

    [Test]
    public void Current_is_null_before_any_enqueue()
    {
        new EnqueueContextAccessor().Current.Should().BeNull();
    }

    [Test]
    public void Entering_exposes_the_context()
    {
        var accessor = new EnqueueContextAccessor();
        var context = AContext();

        using (accessor.Enter(context))
            accessor.Current.Should().BeSameAs(context);
    }

    [Test]
    public void Leaving_the_scope_clears_the_context()
    {
        var accessor = new EnqueueContextAccessor();

        using (accessor.Enter(AContext())) { }

        accessor
            .Current.Should()
            .BeNull(
                "a hook reading Current after the enqueue would write on a context nobody commits"
            );
    }

    [Test]
    public void A_second_enqueue_can_reuse_the_accessor_once_the_first_has_left()
    {
        var accessor = new EnqueueContextAccessor();
        var second = AContext();

        using (accessor.Enter(AContext())) { }

        using (accessor.Enter(second))
            accessor.Current.Should().BeSameAs(second);
    }

    [Test]
    public void An_inner_enqueue_sees_its_own_context_and_restores_the_outer_one()
    {
        var accessor = new EnqueueContextAccessor();
        var outer = AContext();
        var inner = AContext();

        using (accessor.Enter(outer))
        {
            using (accessor.Enter(inner))
                accessor.Current.Should().BeSameAs(inner);

            accessor
                .Current.Should()
                .BeSameAs(
                    outer,
                    "an OnQueue hook that enqueues another train must get its own context back "
                        + "for the rest of its work"
                );
        }

        accessor.Current.Should().BeNull();
    }

    [Test]
    public async Task Concurrent_enqueues_on_one_accessor_each_see_their_own_context()
    {
        var accessor = new EnqueueContextAccessor();
        var first = AContext();
        var second = AContext();
        var bothEntered = new TaskCompletionSource();
        var entered = 0;

        async Task<IDataContext?> Enqueue(IDataContext context)
        {
            using (accessor.Enter(context))
            {
                if (Interlocked.Increment(ref entered) == 2)
                    bothEntered.SetResult();

                // Both flows are inside Enter at once before either reads.
                await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return accessor.Current;
            }
        }

        var seen = await Task.WhenAll(
            Task.Run(() => Enqueue(first)),
            Task.Run(() => Enqueue(second))
        );

        seen[0]
            .Should()
            .BeSameAs(
                first,
                "a scope shared by concurrent enqueues, such as a Blazor circuit, must not hand "
                    + "one enqueue's context to the other"
            );
        seen[1].Should().BeSameAs(second);
    }

    [Test]
    public void An_accessor_from_another_scope_sees_the_same_enqueue()
    {
        var entered = new EnqueueContextAccessor();
        var elsewhere = new EnqueueContextAccessor();
        var context = AContext();

        using (entered.Enter(context))
            elsewhere
                .Current.Should()
                .BeSameAs(
                    context,
                    "a singleton train, or a repository resolved from another scope, reads the "
                        + "enqueue running on this async flow"
                );
    }

    [Test]
    public void Disposing_the_scope_twice_is_harmless()
    {
        var accessor = new EnqueueContextAccessor();
        var scope = accessor.Enter(AContext());

        scope.Dispose();
        var act = () => scope.Dispose();

        act.Should().NotThrow();
        accessor.Current.Should().BeNull();
    }
}
