using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Trax.Api.GraphQL.Mutations;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// A dead-letter acknowledgement's note is at most <see cref="DeadLetterMutations.MaxNoteLength"/>
/// characters, the scheduler's <see cref="TraxScheduler.MaxAcknowledgeNoteLength"/>. The scheduler
/// refuses a longer one for the dashboard and the API alike, before it opens the database, and the
/// mutations return its refusal as it is (Api ADR 0028): a failed result with the reason.
/// </summary>
[TestFixture]
public class DeadLetterNoteLimitTests
{
    private static readonly string OverTheCap = new('n', DeadLetterMutations.MaxNoteLength + 1);

    private static readonly string Refusal =
        $"The note is {DeadLetterMutations.MaxNoteLength + 1} characters; it may be at most "
        + $"{DeadLetterMutations.MaxNoteLength} characters.";

    private IDataContextProviderFactory _database = null!;
    private TraxScheduler _scheduler = null!;

    [SetUp]
    public void SetUp()
    {
        _database = Substitute.For<IDataContextProviderFactory>();
        _scheduler = new TraxScheduler(
            _database,
            Substitute.For<ITrainRegistry>(),
            Substitute.For<ICancellationRegistry>(),
            NullLogger<TraxScheduler>.Instance
        );
    }

    [Test]
    public void TheCap_IsTheSchedulers()
    {
        DeadLetterMutations.MaxNoteLength.Should().Be(TraxScheduler.MaxAcknowledgeNoteLength);
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
        result.Message.Should().Be(Refusal);
        _database.ReceivedCalls().Should().BeEmpty();
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
        result.Message.Should().Be(Refusal);
        _database.ReceivedCalls().Should().BeEmpty();
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
        result.Message.Should().Be(Refusal);
        _database.ReceivedCalls().Should().BeEmpty();
    }

    [Test]
    public async Task EveryAcknowledgement_NoteAtTheCap_IsPassedOn()
    {
        var atTheCap = new string('n', DeadLetterMutations.MaxNoteLength);
        var scheduler = Substitute.For<ITraxScheduler>();
        scheduler
            .AcknowledgeDeadLetterAsync(
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new DeadLetterOperationResult(true, null, "acknowledged"));
        scheduler
            .AcknowledgeDeadLettersAsync(
                Arg.Any<long[]>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new BatchDeadLetterResult(2, "acknowledged"));
        scheduler
            .AcknowledgeAllDeadLettersAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new BatchDeadLetterResult(5, "acknowledged"));
        var mutations = new DeadLetterMutations();

        (await mutations.AcknowledgeDeadLetter(7, atTheCap, scheduler, default))
            .Success.Should()
            .BeTrue();
        (await mutations.AcknowledgeDeadLetters([1, 2], atTheCap, scheduler, default))
            .Count.Should()
            .Be(2);
        (await mutations.AcknowledgeAllDeadLetters(atTheCap, scheduler, default))
            .Count.Should()
            .Be(5);
        await scheduler
            .Received(1)
            .AcknowledgeAllDeadLettersAsync(atTheCap, Arg.Any<CancellationToken>());
    }
}
