using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Recovery.Trains.DecisionJournal;

public interface IDecisionJournalTrain : IServiceTrain<DecisionJournalInput, DecisionJournalOutput>;
