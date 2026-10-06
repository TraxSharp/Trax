using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Trains.DecisionJournal.Junctions;

namespace Trax.Samples.Recovery.Trains.DecisionJournal;

/// <summary>
/// Reads what <c>AddDecisionRecording()</c> wrote for one execution: each decision, whether it was
/// replayed, and why a replay was refused. Junction events say <c>replayed</c>, but not why an answer
/// was asked afresh, so the page reads this to tell "state changed" from "asked afresh on purpose".
/// </summary>
[TraxAuthorize(Roles = RecoveryRoles.Operator)]
[TraxQuery(Description = "The decisions an execution recorded")]
public class DecisionJournalTrain
    : ServiceTrain<DecisionJournalInput, DecisionJournalOutput>,
        IDecisionJournalTrain
{
    protected override Task<Either<Exception, DecisionJournalOutput>> Junctions() =>
        Chain<ReadDecisionJournal>().Resolve();
}
