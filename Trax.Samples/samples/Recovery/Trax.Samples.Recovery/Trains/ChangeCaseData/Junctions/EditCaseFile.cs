using Trax.Effect.Services.EffectJunction;
using Trax.Mediator.Services.Principal;
using Trax.Samples.Recovery.Records;

namespace Trax.Samples.Recovery.Trains.ChangeCaseData.Junctions;

public class EditCaseFile(CaseFiles caseFiles, ICurrentPrincipalProvider caller)
    : EffectJunction<ChangeCaseDataInput, ChangeCaseDataOutput>
{
    public override Task<ChangeCaseDataOutput> Run(ChangeCaseDataInput input) =>
        Task.FromResult(
            new ChangeCaseDataOutput
            {
                RunId = input.RunId,
                Change = caseFiles.Change(input.RunId, caller.GetCurrentPrincipalId()),
            }
        );
}
