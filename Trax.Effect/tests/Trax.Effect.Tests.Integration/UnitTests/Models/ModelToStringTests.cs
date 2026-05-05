using FluentAssertions;
using NUnit.Framework;
using Trax.Effect.Enums;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.BackgroundJob.DTOs;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.Effect.Tests.Integration.UnitTests.Models;

[TestFixture]
public class ModelToStringTests
{
    [Test]
    public void WorkQueue_Create_AndPropertiesAndToString_AllExercised()
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "T",
                Input = "{}",
                InputTypeName = "Trax.Tests.In",
                ManifestId = 5,
                Priority = 3,
                ScheduledAt = DateTime.UtcNow,
                DeadLetterId = null,
            }
        );

        entry.TrainName.Should().Be("T");
        entry.Status.Should().Be(WorkQueueStatus.Queued);
        entry.ManifestId.Should().Be(5);
        entry.Priority.Should().Be(3);
        entry.ScheduledAt.Should().NotBeNull();
        entry.DispatchedAt.Should().BeNull();
        entry.DispatchAttempts.Should().Be(0);
        entry.MetadataId.Should().BeNull();
        entry.Manifest.Should().BeNull();
        entry.Metadata.Should().BeNull();
        entry.DeadLetter.Should().BeNull();
        entry.ToString().Should().NotBeNullOrEmpty().And.Contain("\"T\"");
    }

    [Test]
    public void WorkQueue_Create_PriorityClamped()
    {
        var low = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "T",
                Input = "{}",
                InputTypeName = "X",
                Priority = -50,
            }
        );
        var high = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = "T",
                Input = "{}",
                InputTypeName = "X",
                Priority = 9999,
            }
        );

        low.Priority.Should().BeGreaterThanOrEqualTo(0);
        high.Priority.Should().BeLessThanOrEqualTo(31);
    }

    [Test]
    public void Manifest_Create_AndToString()
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(ModelToStringTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.Once,
                IntervalSeconds = 60,
                Properties = new Sample { Value = "hello" },
            }
        );

        manifest.IsEnabled.Should().BeTrue();
        manifest.ScheduleType.Should().Be(ScheduleType.Once);
        manifest.IntervalSeconds.Should().Be(60);
        manifest.PropertyTypeName.Should().NotBeNullOrEmpty();
        manifest.MaxRetries.Should().BeGreaterThanOrEqualTo(0);
        manifest.ToString().Should().NotBeNullOrEmpty();
    }

    [Test]
    public void ManifestGroup_PropertiesAndToString()
    {
        var group = new ManifestGroup
        {
            Name = "g",
            MaxActiveJobs = 4,
            Priority = 1,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        group.MaxActiveJobs.Should().Be(4);
        group.Priority.Should().Be(1);
        group.IsEnabled.Should().BeTrue();
        group.Manifests.Should().BeEmpty();
        group.ToString().Should().NotBeNullOrEmpty().And.Contain("\"g\"");
    }

    [Test]
    public void DeadLetter_Create_AndToString()
    {
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(ModelToStringTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.Once,
                Properties = new Sample { Value = "v" },
            }
        );
        var dl = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "boom",
                RetryCount = 4,
            }
        );

        dl.Reason.Should().Be("boom");
        dl.Status.Should().Be(DeadLetterStatus.AwaitingIntervention);
        dl.ResolvedAt.Should().BeNull();
        dl.ResolutionNote.Should().BeNull();
        dl.ToString().Should().NotBeNullOrEmpty().And.Contain("\"boom\"");
    }

    [Test]
    public void BackgroundJob_Create_AndProperties()
    {
        var job = BackgroundJob.Create(
            new CreateBackgroundJob
            {
                MetadataId = 99,
                Input = "{\"value\":\"x\"}",
                InputType = "Sample",
                Priority = 1,
            }
        );

        job.MetadataId.Should().Be(99);
        job.InputType.Should().Be("Sample");
        job.ToString().Should().NotBeNullOrEmpty().And.Contain("99");
    }

    private sealed record Sample : IManifestProperties
    {
        public string Value { get; init; } = "";
    }
}
