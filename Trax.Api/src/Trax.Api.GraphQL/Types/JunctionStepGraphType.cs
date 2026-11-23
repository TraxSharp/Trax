using HotChocolate.Types;
using Trax.Api.DTOs;

namespace Trax.Api.GraphQL.Types;

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
        descriptor
            .Field(s => s.NodeId)
            .Description(
                "The id of the declared node this step ran for, as operations.runGraph names "
                    + "it. Null for a step recorded before node ids were, and wherever the step's "
                    + "name is withheld, since the id names the track."
            );
        descriptor
            .Field(s => s.BranchPath)
            .Description(
                "The path of the Parallel branch the step ran in, such as Parallel#0/cocitation, "
                    + "or null outside any branch. Withheld wherever the step's name is."
            );
    }
}
