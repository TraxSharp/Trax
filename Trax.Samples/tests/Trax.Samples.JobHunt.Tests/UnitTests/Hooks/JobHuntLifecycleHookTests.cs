using FluentAssertions;
using HotChocolate.Subscriptions;
using Moq;
using NUnit.Framework;
using Trax.Effect.Models.Metadata;
using Trax.Samples.JobHunt.Hooks;
using Trax.Samples.JobHunt.Subscriptions;
using Trax.Samples.JobHunt.Trains.AddJob;
using Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials;
using Trax.Samples.JobHunt.Trains.MonitorJob;

namespace Trax.Samples.JobHunt.Tests.UnitTests.Hooks;

[TestFixture]
public class JobHuntLifecycleHookTests
{
    private Mock<ITopicEventSender> _sender = null!;
    private JobHuntLifecycleHook _hook = null!;

    [SetUp]
    public void SetUp()
    {
        _sender = new Mock<ITopicEventSender>();
        _hook = new JobHuntLifecycleHook(_sender.Object);
    }

    [Test]
    public async Task OnCompleted_AddJobTrain_PublishesToUserTopic()
    {
        var metadata = MakeMetadata(
            typeof(IAddJobTrain),
            """{"jobId":"11111111-1111-1111-1111-111111111111","userId":"alice","title":"Dev","company":"Co"}"""
        );

        await _hook.OnCompleted(metadata, CancellationToken.None);

        _sender.Verify(
            s =>
                s.SendAsync(
                    "User:alice:jobs",
                    It.Is<JobHuntSubscriptionEvent>(e => e.EventType == "JobAdded"),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Test]
    public async Task OnCompleted_GenerateMaterialsTrain_PublishesToJobMaterialsTopic()
    {
        var jobId = Guid.NewGuid();
        var metadata = MakeMetadata(
            typeof(IGenerateApplicationMaterialsTrain),
            $$$"""{"resumeArtifactId":"{{{Guid.NewGuid()}}}","coverLetterArtifactId":"{{{Guid.NewGuid()}}}","resumeMarkdown":"r","coverLetterMarkdown":"c","jobId":"{{{jobId}}}","userId":"alice"}"""
        );

        await _hook.OnCompleted(metadata, CancellationToken.None);

        _sender.Verify(
            s =>
                s.SendAsync(
                    $"Job:{jobId}:materials",
                    It.Is<JobHuntSubscriptionEvent>(e => e.EventType == "MaterialsGenerated"),
                    It.IsAny<CancellationToken>()
                ),
            Times.Once
        );
    }

    [Test]
    public async Task OnCompleted_UnrelatedTrain_DoesNotPublish()
    {
        var metadata = new Metadata
        {
            Name = "Trax.Samples.JobHunt.Trains.SomeOther.ISomeOtherTrain",
            Output = """{"data":"value"}""",
        };

        await _hook.OnCompleted(metadata, CancellationToken.None);

        _sender.Verify(
            s =>
                s.SendAsync(
                    It.IsAny<string>(),
                    It.IsAny<JobHuntSubscriptionEvent>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Test]
    public async Task OnCompleted_NullOutput_DoesNotPublish()
    {
        var metadata = MakeMetadata(typeof(IAddJobTrain), null);

        await _hook.OnCompleted(metadata, CancellationToken.None);

        _sender.Verify(
            s =>
                s.SendAsync(
                    It.IsAny<string>(),
                    It.IsAny<JobHuntSubscriptionEvent>(),
                    It.IsAny<CancellationToken>()
                ),
            Times.Never
        );
    }

    [Test]
    public async Task OnCompleted_MalformedJson_DoesNotThrow()
    {
        var metadata = MakeMetadata(typeof(IAddJobTrain), "not json");

        var act = () => _hook.OnCompleted(metadata, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static Metadata MakeMetadata(Type trainInterface, string? output)
    {
        return new Metadata
        {
            Name = trainInterface.FullName!,
            Output = output,
            ExternalId = Guid.NewGuid().ToString(),
            EndTime = DateTime.UtcNow,
        };
    }
}
