using Trax.Effect.Services.ServiceTrain;

namespace Trax.Cli.Tests.UnitTests.LexiconTrains;

public interface ICheckRefundTrain : IServiceTrain<RefundInput, RefundOutput>;
