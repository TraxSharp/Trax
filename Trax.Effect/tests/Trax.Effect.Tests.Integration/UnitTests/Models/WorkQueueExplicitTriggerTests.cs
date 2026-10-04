using AwesomeAssertions;
using NUnit.Framework;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.Effect.Tests.Integration.UnitTests.Models;

/// <summary>
/// Which entries a disabled manifest still dispatches: only the ones someone asked for by name.
///
/// <para>Enforces <c>docs/adr/0018-a-disabled-manifest-holds-its-queued-work-except-an-explicit-trigger.md</c>.</para>
/// </summary>
[TestFixture]
[Property(
    "adr",
    "docs/adr/0018-a-disabled-manifest-holds-its-queued-work-except-an-explicit-trigger.md"
)]
public class WorkQueueExplicitTriggerTests
{
    [Test]
    public void A_scheduled_entry_is_not_an_explicit_trigger()
    {
        WorkQueue
            .Create(new CreateWorkQueue { TrainName = "T", ManifestId = 1 })
            .IsExplicitTrigger.Should()
            .BeFalse();
    }

    [Test]
    public void An_entry_created_as_a_trigger_is_an_explicit_trigger()
    {
        WorkQueue
            .Create(
                new CreateWorkQueue
                {
                    TrainName = "T",
                    ManifestId = 1,
                    ExplicitTrigger = true,
                }
            )
            .IsExplicitTrigger.Should()
            .BeTrue();
    }

    [Test]
    public void A_dead_letter_requeue_is_always_an_explicit_trigger()
    {
        WorkQueue
            .Create(
                new CreateWorkQueue
                {
                    TrainName = "T",
                    ManifestId = 1,
                    DeadLetterId = 7,
                }
            )
            .IsExplicitTrigger.Should()
            .BeTrue(
                "an operator requeued that dead letter by name ("
                    + "0018-a-disabled-manifest-holds-its-queued-work-except-an-explicit-trigger.md)"
            );
    }
}
