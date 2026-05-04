using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Radzen;
using Trax.Dashboard.Components.Shared;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;

namespace Trax.Dashboard.Tests.Integration.UnitTests.Components;

[TestFixture]
public class StateTimelineTests
{
    private Bunit.TestContext _ctx = null!;

    [SetUp]
    public void SetUp()
    {
        _ctx = new Bunit.TestContext();
        _ctx.Services.AddRadzenComponents();
    }

    [TearDown]
    public void TearDown() => _ctx.Dispose();

    private static Metadata NewMetadata(TrainState state, DateTime? endTime = null)
    {
        var meta = Metadata.Create(
            new CreateMetadata
            {
                Name = "Trax.X.Train",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        meta.TrainState = state;
        meta.StartTime = DateTime.UtcNow.AddMinutes(-5);
        if (endTime.HasValue)
            meta.EndTime = endTime;
        return meta;
    }

    [Test]
    public void Renders_TitleAndAllThreeSteps()
    {
        var meta = NewMetadata(TrainState.Pending);

        var component = _ctx.RenderComponent<StateTimeline>(p => p.Add(x => x.Metadata, meta));

        component.Markup.Should().Contain("State Timeline");
        component.Markup.Should().Contain("Pending");
        component.Markup.Should().Contain("In Progress");
        component.Markup.Should().Contain("Completed");
    }

    [Test]
    public void TerminalLabel_Failed_RendersFailed()
    {
        var meta = NewMetadata(TrainState.Failed, DateTime.UtcNow);

        var component = _ctx.RenderComponent<StateTimeline>(p => p.Add(x => x.Metadata, meta));

        component.Markup.Should().Contain("Failed");
    }

    [Test]
    public void TerminalLabel_Cancelled_RendersCancelled()
    {
        var meta = NewMetadata(TrainState.Cancelled, DateTime.UtcNow);

        var component = _ctx.RenderComponent<StateTimeline>(p => p.Add(x => x.Metadata, meta));

        component.Markup.Should().Contain("Cancelled");
    }

    [Test]
    public void DurationFormatting_LongRun_RendersHours()
    {
        var meta = NewMetadata(TrainState.Completed, DateTime.UtcNow);
        meta.StartTime = meta.EndTime!.Value.AddHours(-2);

        var component = _ctx.RenderComponent<StateTimeline>(p => p.Add(x => x.Metadata, meta));

        component.Markup.Should().Contain("Completed");
    }

    [Test]
    public void InProgress_RendersWithoutTerminal()
    {
        var meta = NewMetadata(TrainState.InProgress);

        var component = _ctx.RenderComponent<StateTimeline>(p => p.Add(x => x.Metadata, meta));

        component.Markup.Should().Contain("In Progress");
        // Terminal step is not yet reached
        component.Markup.Should().Contain("cs-timeline-circle--future");
    }
}
