using System.Text.Json;
using AwesomeAssertions;
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

    [TestCase(" ")]
    [TestCase("   ")]
    [TestCase("\t\r\n")]
    [TestCase("\u00A0\u3000")]
    public void Create_WithAWhitespaceOnlyKey_IsRefused(string key)
    {
        var act = () => WorkQueue.Create(new CreateWorkQueue { TrainName = "T", SubjectKey = key });

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage(
                "*cannot be empty or whitespace*",
                "a blank key is an unset identity as surely as an empty one, and every entry "
                    + "carrying it would be serialized against every other"
            );
    }

    [Test]
    public void Create_WithWhitespaceAroundAKey_KeepsItAsGiven()
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue { TrainName = "T", SubjectKey = " customer-7 " }
        );

        entry.SubjectKey.Should().Be(" customer-7 ", "the key is opaque and compared exactly");
    }

    [Test]
    public void Create_WithAKeyOfSurrogatePairsAtTheLimit_KeepsIt()
    {
        // Each emoji is one character but two UTF-16 units and four UTF-8 bytes.
        var key = string.Concat(Enumerable.Repeat("\U0001F600", WorkQueue.MaxSubjectKeyLength));
        key.Length.Should().Be(2 * WorkQueue.MaxSubjectKeyLength);

        var entry = WorkQueue.Create(new CreateWorkQueue { TrainName = "T", SubjectKey = key });

        entry
            .SubjectKey.Should()
            .Be(key, "the limit counts characters, not the UTF-16 units that encode them");
    }

    [Test]
    public void Create_WithAKeyOfSurrogatePairsOverTheLimit_IsRefused()
    {
        var key = string.Concat(Enumerable.Repeat("\U0001F600", WorkQueue.MaxSubjectKeyLength + 1));

        var act = () => WorkQueue.Create(new CreateWorkQueue { TrainName = "T", SubjectKey = key });

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage(
                $"*{WorkQueue.MaxSubjectKeyLength + 1} characters*limit of "
                    + $"{WorkQueue.MaxSubjectKeyLength}*",
                "the message counts what the caller sees as characters"
            );
    }

    // Built in code rather than passed through [TestCase]: attribute arguments are stored as UTF-8,
    // which turns a lone surrogate into U+FFFD before the test ever sees it.
    private static IEnumerable<TestCaseData> LoneSurrogates()
    {
        yield return new TestCaseData("order:" + '\uD800' + "x", 6).SetName("A high half");
        yield return new TestCaseData("order:" + '\uDC00', 6).SetName("A low half at the end");
        yield return new TestCaseData(new string('\uD83D', 1), 0).SetName("Only a high half");
        yield return new TestCaseData("order:" + '\uDE00' + '\uD83D', 6).SetName(
            "Two halves in the wrong order"
        );
    }

    [TestCaseSource(nameof(LoneSurrogates))]
    public void Create_WithALoneSurrogate_IsRefused(string key, int index)
    {
        var act = () => WorkQueue.Create(new CreateWorkQueue { TrainName = "T", SubjectKey = key });

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage(
                $"*unpaired surrogate at position {index}*",
                "the database encodes the key as UTF-8, which has no encoding for half a "
                    + "character, so the insert would fail far from the caller"
            );
    }

    [Test]
    public void Create_WithAPairedSurrogate_KeepsIt()
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue { TrainName = "T", SubjectKey = "order:\U0001F600" }
        );

        entry.SubjectKey.Should().Be("order:\U0001F600");
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
    public void TheSubjectKey_HasNoPublicSetter()
    {
        typeof(WorkQueue)
            .GetProperty(nameof(WorkQueue.SubjectKey))!
            .GetSetMethod(nonPublic: false)
            .Should()
            .BeNull(
                "a key set after Create would skip the checks Create makes, so an empty or "
                    + "over-long key could reach the dispatcher"
            );
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
