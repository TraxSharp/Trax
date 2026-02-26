using Trax.Effect.Data.Services.DataContext;
using Trax.Core.Step;
using LanguageExt;

namespace Trax.Effect.Data.Steps.CommitTransaction;

/// <summary>
/// Built-in step allowing for transactions to be committed.
/// </summary>
public class CommitTransaction(IDataContext dataContextFactory) : Step<Unit, Unit>
{
    public override async Task<Unit> Run(Unit input)
    {
        await dataContextFactory.CommitTransaction();

        return Unit.Default;
    }
}
