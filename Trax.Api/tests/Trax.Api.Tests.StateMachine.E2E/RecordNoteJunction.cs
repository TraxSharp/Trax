using System.Globalization;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.StateMachine.E2E;

public class RecordNoteJunction : Junction<RecordNoteInput, RecordNoteOutput>
{
    public override Task<RecordNoteOutput> Run(RecordNoteInput input) =>
        Task.FromResult(new RecordNoteOutput { Text = input.Text });
}
