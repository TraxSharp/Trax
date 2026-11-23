using System.Globalization;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.StateMachine.E2E;

[TraxAuthorize(Roles = "Approver")]
[TraxMutation(Description = "Echoes a note; only an approver may run it.")]
public class RecordNote : ServiceTrain<RecordNoteInput, RecordNoteOutput>, IRecordNote
{
    protected override Task<Either<Exception, RecordNoteOutput>> Junctions() =>
        Chain<RecordNoteJunction>().Resolve();
}
