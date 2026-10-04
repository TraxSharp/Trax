using AwesomeAssertions;
using NSubstitute;
using Trax.Api.GraphQL.Mutations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// A dead-letter acknowledgement's note is at most <see cref="DeadLetterMutations.MaxNoteLength"/>
/// characters. A longer one is a refusal (Api ADR 0028): a failed result with the reason, and
/// nothing asked of the scheduler.
/// </summary>
[TestFixture]
public class DeadLetterNoteLimitTests
{
    private static readonly string AtTheCap = new('n', DeadLetterMutations.MaxNoteLength);
    private static readonly string OverTheCap = new('n', DeadLetterMutations.MaxNoteLength + 1);

    private ITraxScheduler _scheduler = null!;

    [SetUp]
    public void SetUp()
    {
        _scheduler = Substitute.For<ITraxScheduler>();
        _scheduler
            .AcknowledgeDeadLetterAsync(
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new DeadLetterOperationResult(true, null, "acknowledged"));
        _scheduler
            .AcknowledgeDeadLettersAsync(
                Arg.Any<long[]>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new BatchDeadLetterResult(2, "acknowledged"));
        _scheduler
            .AcknowledgeAllDeadLettersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BatchDeadLetterResult(5, "acknowledged"));
    }

    [Test]
    public async Task AcknowledgeDeadLetter_NoteOverTheCap_IsRefused()
    {
        var result = await new DeadLetterMutations().AcknowledgeDeadLetter(
            7,
            OverTheCap,
            _scheduler,
            default
        );

        result.Success.Should().BeFalse();
        result.WorkQueueId.Should().BeNull();
        result.Message.Should().Contain($"at most {DeadLetterMutations.MaxNoteLength} characters");
        _scheduler.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task AcknowledgeDeadLetters_NoteOverTheCap_IsRefused()
    {
        var result = await new DeadLetterMutations().AcknowledgeDeadLetters(
            [1, 2],
            OverTheCap,
            _scheduler,
            default
        );

        result.Count.Should().Be(0);
        result.Message.Should().Contain($"at most {DeadLetterMutations.MaxNoteLength} characters");
        _scheduler.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task AcknowledgeAllDeadLetters_NoteOverTheCap_IsRefused()
    {
        var result = await new DeadLetterMutations().AcknowledgeAllDeadLetters(
            OverTheCap,
            _scheduler,
            default
        );

        result.Count.Should().Be(0);
        result.Message.Should().Contain($"at most {DeadLetterMutations.MaxNoteLength} characters");
        _scheduler.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task EveryAcknowledgement_NoteAtTheCap_IsPassedOn()
    {
        var mutations = new DeadLetterMutations();

        (await mutations.AcknowledgeDeadLetter(7, AtTheCap, _scheduler, default))
            .Success.Should()
            .BeTrue();
        (await mutations.AcknowledgeDeadLetters([1, 2], AtTheCap, _scheduler, default))
            .Count.Should()
            .Be(2);
        (await mutations.AcknowledgeAllDeadLetters(AtTheCap, _scheduler, default))
            .Count.Should()
            .Be(5);
    }
}
