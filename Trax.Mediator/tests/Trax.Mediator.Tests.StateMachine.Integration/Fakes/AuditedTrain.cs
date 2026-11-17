using System.Text.Json.Nodes;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>Two role sets: a caller needs <c>Operator</c> and also <c>Auditor</c>.</summary>
[TraxAuthorize(Roles = "Operator")]
[TraxAuthorize(Roles = "Auditor")]
public class AuditedTrain : ServiceTrain<AuditedInput, JobOutput>, IAuditedTrain
{
    protected override Task<Either<Exception, JobOutput>> Junctions() =>
        Chain<AuditedStep>().Resolve();
}
