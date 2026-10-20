using AwesomeAssertions;
using Trax.Effect.Attributes;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The copy of an invoked run's output that its machine reads is never written when the output reaches a
/// <c>[TraxSensitive]</c> member: the startup check and the launch refuse such a train, and the run's terminal write
/// refuses it once more, so the value is not stored unmasked. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
public class InvokedRunOutputTests
{
    private sealed record Pointer(string Artifact);

    private sealed record Secret(string Artifact, [property: TraxSensitive] string ApiKey);

    private static Metadata InvokedRun() =>
        Metadata.Create(
            new CreateMetadata
            {
                Name = "Fetch",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                InvokedBy = new InvokedBy("ingest", Guid.NewGuid(), SnapshotOwnerKind.System),
            }
        );

    [Test]
    public void A_sensitive_output_is_not_recorded_for_the_machine()
    {
        var run = InvokedRun();

        var failure = run.RecordInvokeOutput(new Secret("a", "key-123"), typeof(Secret));

        failure
            .Should()
            .BeOfType<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("[TraxSensitive]");
        run.InvokeOutput.Should()
            .BeNull("an unrecorded output is applied as the state's failure, never stored");
        run.InvokeOutputOversize.Should().BeFalse();
    }

    [Test]
    public void A_pointer_output_is_recorded()
    {
        var run = InvokedRun();

        run.RecordInvokeOutput(new Pointer("s3://a"), typeof(Pointer)).Should().BeNull();
        run.InvokeOutput.Should().Be("""{"artifact":"s3://a"}""");
    }
}
