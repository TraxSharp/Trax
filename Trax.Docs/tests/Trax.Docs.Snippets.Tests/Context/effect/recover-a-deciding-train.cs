// What the compiled examples on effect/recover-a-deciding-train.md take as given.
global using static RecoverPage;
global using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Services.ServiceTrain;
using Trax.Scheduler.Services.TraxScheduler;

public record LoanApplication(string Id, int CreditScore) : IManifestProperties;

public record LoanDecision(bool Approved);

public interface IUnderwriteLoanTrain : IServiceTrain<LoanApplication, LoanDecision>;

public static class RecoverPage
{
    public static ITraxScheduler scheduler => throw new NotSupportedException();

    public static IDataContextProviderFactory dataContextFactory =>
        throw new NotSupportedException();

    public static long metadataId => 1;
}
