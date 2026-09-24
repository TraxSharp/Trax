using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.Effect.Tests.Integration.UnitTests.Models;

/// <summary>
/// <see cref="WorkQueue.Create"/> is the only way to build an entry, so it is where a subject key
/// that the index or the dispatcher cannot handle is refused, whoever builds the entry.
/// </summary>
[TestFixture]
[Property("adr", "Trax.Docs/adr/0019-queued-work-for-one-subject-runs-one-at-a-time.md")]
public class WorkQueueCreateTests
{
    [Test]
    public void Create_WithNoSubjectKey_LeavesTheEntryUnserialized()
    {
        var entry = WorkQueue.Create(new CreateWorkQueue { TrainName = "T" });

        entry.SubjectKey.Should().BeNull();
    }

    [Test]
    public void Create_WithAKeyAtTheLimit_KeepsIt()
    {
        var key = new string('k', WorkQueue.MaxSubjectKeyLength);

        var entry = WorkQueue.Create(new CreateWorkQueue { TrainName = "T", SubjectKey = key });

        entry.SubjectKey.Should().Be(key);
    }

    [Test]
    public void Create_WithAnEmptyKey_IsRefused()
    {
        var act = () => WorkQueue.Create(new CreateWorkQueue { TrainName = "T", SubjectKey = "" });

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage(
                "*cannot be empty*",
                "every entry carrying an empty key would be serialized against every other"
            );
    }

    [Test]
    public void Create_WithAKeyOverTheLimit_IsRefused()
    {
        var key = new string('k', WorkQueue.MaxSubjectKeyLength + 1);

        var act = () => WorkQueue.Create(new CreateWorkQueue { TrainName = "T", SubjectKey = key });

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage(
                $"*{WorkQueue.MaxSubjectKeyLength + 1} characters*limit of "
                    + $"{WorkQueue.MaxSubjectKeyLength}*",
                "a key too long for the index inserts fine and then fails every claim"
            );
    }

    [Test]
    public void AnEntry_RoundTripsThroughJson_WithoutAPublicConstructor()
    {
        // The parameterless constructor is protected so an entry cannot be built without
        // Create, but deserialization still has to reach it through [JsonConstructor].
        var entry = WorkQueue.Create(
            new CreateWorkQueue { TrainName = "Some.Train", SubjectKey = "customer-7" }
        );

        var read = JsonSerializer.Deserialize<WorkQueue>(JsonSerializer.Serialize(entry));

        read.Should().NotBeNull();
        read!.TrainName.Should().Be("Some.Train");
        read.SubjectKey.Should().Be("customer-7");
        read.ConfirmedAt.Should().Be(entry.ConfirmedAt);
    }

    [Test]
    public void TheParameterlessConstructor_IsNotPublic()
    {
        typeof(WorkQueue)
            .GetConstructor(Type.EmptyTypes)
            .Should()
            .BeNull(
                "an entry built with it leaves ConfirmedAt null, so it would be saved as staged "
                    + "and never dispatched; WorkQueue.Create is the way to build one"
            );
    }
}
