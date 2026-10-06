using HotChocolate.Types;
using Trax.Api.DTOs;

namespace Trax.Api.GraphQL.Types;

/// <summary>
/// The GraphQL shape of <see cref="JunctionEvent"/>. Fields are bound explicitly, so nothing added
/// to the record later reaches the schema without a decision here.
/// </summary>
internal sealed class JunctionEventGraphType : ObjectType<JunctionEvent>
{
    /// <inheritdoc/>
    protected override void Configure(IObjectTypeDescriptor<JunctionEvent> descriptor)
    {
        descriptor.Name("JunctionEvent");
        descriptor.BindFieldsExplicitly();

        descriptor.Field(e => e.MetadataId);
        descriptor.Field(e => e.ExternalId);
        descriptor.Field(e => e.TrainName);
        descriptor.Field(e => e.EventType);
        descriptor.Field(e => e.Timestamp);
        descriptor.Field(e => e.Junction).Type<NonNullType<JunctionStepGraphType>>();
        descriptor
            .Field(e => e.Sequence)
            .Description(
                "This event's position in the subscription: 1 for the first event and one more for "
                    + "each after it. A jump of more than one means events were lost on the way to "
                    + "this subscriber. The feed is shared by every run on the host, so a gap can "
                    + "come from another run's steps rather than this one's. In the operations view "
                    + "read operations.junctionRuns again to recover; a broadcast subscriber cannot "
                    + "read it, and has no way to recover a gap. The live feed is lossy under load."
            );
    }
}

/// <summary>
/// The GraphQL shape of <see cref="JunctionStep"/>, shared by the <c>onJunctionEvent</c>
/// subscription and the <c>operations.junctionRuns</c> query. Fields are bound explicitly.
/// </summary>
internal sealed class JunctionStepGraphType : ObjectType<JunctionStep>
{
    /// <inheritdoc/>
    protected override void Configure(IObjectTypeDescriptor<JunctionStep> descriptor)
    {
        descriptor.Name("JunctionStep");
        descriptor.BindFieldsExplicitly();

        descriptor.Field(s => s.Position);
        descriptor.Field(s => s.Kind);
        descriptor.Field(s => s.Name);
        descriptor.Field(s => s.State);
        descriptor.Field(s => s.StartedAt);
        descriptor.Field(s => s.EndedAt);
        descriptor.Field(s => s.DurationMs);
        descriptor.Field(s => s.FailureClass);
        descriptor.Field(s => s.FailureException);
        descriptor.Field(s => s.QuestionKey);
        descriptor.Field(s => s.Answer);
        descriptor.Field(s => s.Confidence);
        descriptor.Field(s => s.Replayed);
        descriptor.Field(s => s.Decider);
        descriptor.Field(s => s.AnswerWithheld);
        descriptor.Field(s => s.Attempt);
        descriptor.Field(s => s.NameWithheld);
        descriptor.Field(s => s.TrackPosition);
    }
}
