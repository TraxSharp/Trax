using System.Globalization;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.StateMachine.E2E;

// A mutation train only an approver may run, so a host's train documents include one that is role-gated.

public record RecordNoteInput
{
    public string Text { get; init; } = "";
}
