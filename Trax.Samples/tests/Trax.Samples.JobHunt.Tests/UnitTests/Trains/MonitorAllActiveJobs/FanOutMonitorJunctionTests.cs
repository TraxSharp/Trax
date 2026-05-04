using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Trax.Mediator.Services.TrainBus;
using Trax.Samples.JobHunt.Data.Entities;
using Trax.Samples.JobHunt.Tests.Fixtures;
using Trax.Samples.JobHunt.Trains.MonitorAllActiveJobs;
using Trax.Samples.JobHunt.Trains.MonitorAllActiveJobs.Junctions;
using Trax.Samples.JobHunt.Trains.MonitorJob;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Trains.MonitorAllActiveJobs;

[TestFixture]
public class FanOutMonitorJunctionTests
{
    private static Job NewJob(JobStatus status, string? url) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = "u1",
            Title = "Engineer",
            Company = "Acme",
            Url = url,
            RawDescription = "x",
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

    [Test]
    public async Task Run_NoActiveJobs_ReturnsZero()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var bus = new Mock<ITrainBus>(MockBehavior.Strict);
        var junction = new FanOutMonitorJunction(
            db,
            bus.Object,
            NullLogger<FanOutMonitorJunction>.Instance
        );

        var output = await junction.Run(new MonitorAllActiveJobsInput());

        output.JobsChecked.Should().Be(0);
        output.JobsChanged.Should().Be(0);
        output.JobsClosed.Should().Be(0);
        bus.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Run_FiltersOutInactiveOrUrlless_OnlyMonitorsEligible()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var active = NewJob(JobStatus.Active, "https://example.com/a");
        var activeNoUrl = NewJob(JobStatus.Active, null);
        var closed = NewJob(JobStatus.Closed, "https://example.com/b");
        db.Jobs.AddRange(active, activeNoUrl, closed);
        await db.SaveChangesAsync();

        var bus = new Mock<ITrainBus>();
        bus.Setup(b =>
                b.RunAsync<MonitorJobOutput>(
                    It.IsAny<object>(),
                    It.IsAny<Trax.Effect.Models.Metadata.Metadata>()
                )
            )
            .ReturnsAsync(new MonitorJobOutput { Changed = false, Closed = false });

        var junction = new FanOutMonitorJunction(
            db,
            bus.Object,
            NullLogger<FanOutMonitorJunction>.Instance
        );

        var output = await junction.Run(new MonitorAllActiveJobsInput());

        output.JobsChecked.Should().Be(1);
        bus.Verify(
            b =>
                b.RunAsync<MonitorJobOutput>(
                    It.IsAny<object>(),
                    It.IsAny<Trax.Effect.Models.Metadata.Metadata>()
                ),
            Times.Once
        );
    }

    [Test]
    public async Task Run_AggregatesChangedAndClosedCounts()
    {
        await using var db = JobHuntDbContextFixture.Create();
        var jobs = Enumerable
            .Range(0, 5)
            .Select(_ => NewJob(JobStatus.Active, "https://x.example/a"))
            .ToList();
        db.Jobs.AddRange(jobs);
        await db.SaveChangesAsync();

        var outputs = new Queue<MonitorJobOutput>(
            new[]
            {
                new MonitorJobOutput { Changed = true, Closed = false },
                new MonitorJobOutput { Changed = false, Closed = true },
                new MonitorJobOutput { Changed = true, Closed = true },
                new MonitorJobOutput { Changed = false, Closed = false },
                new MonitorJobOutput { Changed = true, Closed = false },
            }
        );

        var bus = new Mock<ITrainBus>();
        bus.Setup(b =>
                b.RunAsync<MonitorJobOutput>(
                    It.IsAny<object>(),
                    It.IsAny<Trax.Effect.Models.Metadata.Metadata>()
                )
            )
            .ReturnsAsync(() => outputs.Dequeue());

        var junction = new FanOutMonitorJunction(
            db,
            bus.Object,
            NullLogger<FanOutMonitorJunction>.Instance
        );

        var output = await junction.Run(new MonitorAllActiveJobsInput());

        output.JobsChecked.Should().Be(5);
        output.JobsChanged.Should().Be(3);
        output.JobsClosed.Should().Be(2);
    }
}
