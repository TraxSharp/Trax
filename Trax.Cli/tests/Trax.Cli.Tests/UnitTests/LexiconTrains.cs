using Trax.Effect.Services.ServiceTrain;

namespace Trax.Cli.Tests.UnitTests.LexiconTrains;

// A train whose records the lexicon tests give the CLI with --trains, so a Runs: line can leave its fields out.
public sealed record RefundInput(string Reason);

public sealed record RefundOutput(bool Approved);

public interface ICheckRefundTrain : IServiceTrain<RefundInput, RefundOutput>;
