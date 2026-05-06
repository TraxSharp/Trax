using FluentAssertions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.UnitTests;

[TestFixture]
public class InMemoryJobSubmitterTests
{
    [Test]
    public async Task EnqueueAsync_MetadataIdOnly_RunsTrainAndReturnsPrefixedId()
    {
        var train = new RecordingJobRunnerTrain();
        var submitter = new InMemoryJobSubmitter(train);

        var jobId = await submitter.EnqueueAsync(metadataId: 42);

        jobId.Should().Be("inmemory-1");
        train.Calls.Should().HaveCount(1);
        train.Calls[0].MetadataId.Should().Be(42);
        train.Calls[0].Input.Should().BeNull();
    }

    [Test]
    public async Task EnqueueAsync_MetadataIdOnly_WithCancellationToken_PassesTokenThrough()
    {
        var train = new RecordingJobRunnerTrain();
        var submitter = new InMemoryJobSubmitter(train);
        using var cts = new CancellationTokenSource();

        var jobId = await submitter.EnqueueAsync(metadataId: 7, cts.Token);

        jobId.Should().Be("inmemory-1");
        train.Calls[0].Token.Should().Be(cts.Token);
    }

    [Test]
    public async Task EnqueueAsync_WithInput_RunsTrainWithInputAndReturnsPrefixedId()
    {
        var train = new RecordingJobRunnerTrain();
        var submitter = new InMemoryJobSubmitter(train);
        var input = new { Foo = "bar" };

        var jobId = await submitter.EnqueueAsync(metadataId: 1, input);

        jobId.Should().Be("inmemory-1");
        train.Calls[0].Input.Should().BeSameAs(input);
    }

    [Test]
    public async Task EnqueueAsync_WithInputAndCancellationToken_PassesBothThrough()
    {
        var train = new RecordingJobRunnerTrain();
        var submitter = new InMemoryJobSubmitter(train);
        var input = new { Bar = 5 };
        using var cts = new CancellationTokenSource();

        await submitter.EnqueueAsync(metadataId: 9, input, cts.Token);

        train.Calls[0].MetadataId.Should().Be(9);
        train.Calls[0].Input.Should().BeSameAs(input);
        train.Calls[0].Token.Should().Be(cts.Token);
    }

    [Test]
    public async Task EnqueueAsync_MultipleCalls_IncrementsJobIdCounter()
    {
        var train = new RecordingJobRunnerTrain();
        var submitter = new InMemoryJobSubmitter(train);

        var id1 = await submitter.EnqueueAsync(1);
        var id2 = await submitter.EnqueueAsync(2);
        var id3 = await submitter.EnqueueAsync(3);

        id1.Should().Be("inmemory-1");
        id2.Should().Be("inmemory-2");
        id3.Should().Be("inmemory-3");
        train.Calls.Should().HaveCount(3);
    }

    [Test]
    public async Task EnqueueAsync_TrainThrows_PropagatesException()
    {
        var train = new RecordingJobRunnerTrain
        {
            Throw = new InvalidOperationException("train-fail"),
        };
        var submitter = new InMemoryJobSubmitter(train);

        var act = async () => await submitter.EnqueueAsync(metadataId: 1);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("train-fail");
    }

    private sealed class RecordingJobRunnerTrain : IJobRunnerTrain
    {
        public List<(long MetadataId, object? Input, CancellationToken Token)> Calls { get; } = [];
        public Exception? Throw { get; set; }

        public Trax.Effect.Models.Metadata.Metadata? Metadata => null;

        public Task<LanguageExt.Unit> Run(
            RunJobRequest input,
            CancellationToken cancellationToken = default
        )
        {
            Calls.Add((input.MetadataId, input.Input, cancellationToken));
            if (Throw is not null)
                throw Throw;
            return Task.FromResult(LanguageExt.Unit.Default);
        }

        public void Dispose() { }
    }
}
