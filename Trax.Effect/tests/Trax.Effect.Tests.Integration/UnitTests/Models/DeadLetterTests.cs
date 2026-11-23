using AwesomeAssertions;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;

namespace Trax.Effect.Tests.Integration.UnitTests.Models;

[TestFixture]
public class DeadLetterTests
{
    [Test]
    public void Create_ShouldSetPropertiesCorrectly()
    {
        // Arrange
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        var reason = "Max retries exceeded";
        var retryCount = 5;

        // Act
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = reason,
                RetryCount = retryCount,
            },
            TestTime.Clock
        );

        // Assert
        deadLetter.ManifestId.Should().Be(manifest.Id);
        deadLetter.Manifest.Should().NotBeNull();
        deadLetter.Reason.Should().Be(reason);
        deadLetter.RetryCountAtDeadLetter.Should().Be(retryCount);
        deadLetter.Status.Should().Be(DeadLetterStatus.AwaitingIntervention);
        deadLetter.DeadLetteredAt.Should().Be(TestTime.UtcDateTime);
        deadLetter.ResolvedAt.Should().BeNull();
        deadLetter.ResolutionNote.Should().BeNull();
        deadLetter.RetryMetadataId.Should().BeNull();
    }

    [Test]
    public void Create_WithZeroRetries_ShouldSetRetryCountToZero()
    {
        // Arrange
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );

        // Act
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Non-retryable exception",
                RetryCount = 0,
            },
            TestTime.Clock
        );

        // Assert
        deadLetter.RetryCountAtDeadLetter.Should().Be(0);
    }

    [Test]
    public void Acknowledge_ShouldSetStatusAndResolutionDetails()
    {
        // Arrange
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Max retries exceeded",
                RetryCount = 3,
            },
            TestTime.Clock
        );
        var note = "Data was manually corrected in the database";

        // Act
        deadLetter.Acknowledge(note, TestTime.Clock);

        // Assert
        deadLetter.Status.Should().Be(DeadLetterStatus.Acknowledged);
        deadLetter.ResolutionNote.Should().Be(note);
        deadLetter.ResolvedAt.Should().NotBeNull();
        deadLetter.ResolvedAt.Should().Be(TestTime.UtcDateTime);
        deadLetter.RetryMetadataId.Should().BeNull(); // Not retried, so no retry metadata
    }

    [Test]
    public void MarkRetried_ShouldSetStatusAndRetryMetadataId()
    {
        // Arrange
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Max retries exceeded",
                RetryCount = 3,
            },
            TestTime.Clock
        );
        var retryMetadataId = 42;

        // Act
        deadLetter.MarkRetried(retryMetadataId, TestTime.Clock);

        // Assert
        deadLetter.Status.Should().Be(DeadLetterStatus.Retried);
        deadLetter.RetryMetadataId.Should().Be(retryMetadataId);
        deadLetter.ResolvedAt.Should().NotBeNull();
        deadLetter.ResolvedAt.Should().Be(TestTime.UtcDateTime);
        deadLetter.ResolutionNote.Should().BeNull(); // Retried, not acknowledged
    }

    [Test]
    public void StatusTransition_FromAwaitingIntervention_ToAcknowledged()
    {
        // Arrange
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Test reason",
                RetryCount = 1,
            },
            TestTime.Clock
        );

        // Assert initial state
        deadLetter.Status.Should().Be(DeadLetterStatus.AwaitingIntervention);

        // Act
        deadLetter.Acknowledge("Issue resolved out-of-band", TestTime.Clock);

        // Assert final state
        deadLetter.Status.Should().Be(DeadLetterStatus.Acknowledged);
    }

    [Test]
    public void StatusTransition_FromAwaitingIntervention_ToRetried()
    {
        // Arrange
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Test reason",
                RetryCount = 1,
            },
            TestTime.Clock
        );

        // Assert initial state
        deadLetter.Status.Should().Be(DeadLetterStatus.AwaitingIntervention);

        // Act
        deadLetter.MarkRetried(123, TestTime.Clock);

        // Assert final state
        deadLetter.Status.Should().Be(DeadLetterStatus.Retried);
    }

    #region Requeue and LinkRetryMetadata

    [Test]
    public void Requeue_SetsStatusRetriedAndResolvedAt()
    {
        // Arrange
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Max retries exceeded",
                RetryCount = 3,
            },
            TestTime.Clock
        );
        var note = "Re-queued via dashboard (WorkQueue 42)";

        // Act
        deadLetter.Requeue(note, TestTime.Clock);

        // Assert
        deadLetter.Status.Should().Be(DeadLetterStatus.Retried);
        deadLetter.ResolutionNote.Should().Be(note);
        deadLetter.ResolvedAt.Should().NotBeNull();
        deadLetter.ResolvedAt.Should().Be(TestTime.UtcDateTime);
        deadLetter.RetryMetadataId.Should().BeNull();
    }

    [Test]
    public void Requeue_DoesNotRequireMetadataId()
    {
        // Arrange
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Max retries exceeded",
                RetryCount = 3,
            },
            TestTime.Clock
        );

        // Act
        deadLetter.Requeue("Requeued for retry", TestTime.Clock);

        // Assert — RetryMetadataId is NOT set; it gets linked later by the dispatcher
        deadLetter.RetryMetadataId.Should().BeNull();
        deadLetter.Status.Should().Be(DeadLetterStatus.Retried);
    }

    [Test]
    public void LinkRetryMetadata_SetsRetryMetadataId()
    {
        // Arrange
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Max retries exceeded",
                RetryCount = 3,
            },
            TestTime.Clock
        );

        // Requeue first (the normal flow)
        deadLetter.Requeue("Re-queued via dashboard", TestTime.Clock);

        // Act — dispatcher links the metadata after creating it
        deadLetter.LinkRetryMetadata(99);

        // Assert
        deadLetter.RetryMetadataId.Should().Be(99);
        deadLetter.Status.Should().Be(DeadLetterStatus.Retried);
    }

    [Test]
    public void StatusTransition_FromAwaitingIntervention_ToRetriedViaRequeue()
    {
        // Arrange
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeadLetterTests),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
            }
        );
        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "Test reason",
                RetryCount = 1,
            },
            TestTime.Clock
        );

        // Assert initial state
        deadLetter.Status.Should().Be(DeadLetterStatus.AwaitingIntervention);

        // Act
        deadLetter.Requeue("Requeued", TestTime.Clock);

        // Assert final state
        deadLetter.Status.Should().Be(DeadLetterStatus.Retried);
    }

    #endregion
}
