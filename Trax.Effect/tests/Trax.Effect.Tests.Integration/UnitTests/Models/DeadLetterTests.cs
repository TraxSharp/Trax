using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using FluentAssertions;

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
            }
        );

        // Assert
        deadLetter.ManifestId.Should().Be(manifest.Id);
        deadLetter.Manifest.Should().NotBeNull();
        deadLetter.Reason.Should().Be(reason);
        deadLetter.RetryCountAtDeadLetter.Should().Be(retryCount);
        deadLetter.Status.Should().Be(DeadLetterStatus.AwaitingIntervention);
        deadLetter.DeadLetteredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
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
            }
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
            }
        );
        var note = "Data was manually corrected in the database";

        // Act
        deadLetter.Acknowledge(note);

        // Assert
        deadLetter.Status.Should().Be(DeadLetterStatus.Acknowledged);
        deadLetter.ResolutionNote.Should().Be(note);
        deadLetter.ResolvedAt.Should().NotBeNull();
        deadLetter.ResolvedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
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
            }
        );
        var retryMetadataId = 42;

        // Act
        deadLetter.MarkRetried(retryMetadataId);

        // Assert
        deadLetter.Status.Should().Be(DeadLetterStatus.Retried);
        deadLetter.RetryMetadataId.Should().Be(retryMetadataId);
        deadLetter.ResolvedAt.Should().NotBeNull();
        deadLetter.ResolvedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
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
            }
        );

        // Assert initial state
        deadLetter.Status.Should().Be(DeadLetterStatus.AwaitingIntervention);

        // Act
        deadLetter.Acknowledge("Issue resolved out-of-band");

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
            }
        );

        // Assert initial state
        deadLetter.Status.Should().Be(DeadLetterStatus.AwaitingIntervention);

        // Act
        deadLetter.MarkRetried(123);

        // Assert final state
        deadLetter.Status.Should().Be(DeadLetterStatus.Retried);
    }
}
