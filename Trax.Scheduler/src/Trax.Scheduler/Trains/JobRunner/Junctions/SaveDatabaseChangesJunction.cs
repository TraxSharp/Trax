using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Services.EffectJunction;

namespace Trax.Scheduler.Trains.JobRunner.Junctions;

/// <summary>
/// Updates the Manifest's LastSuccessfulRun timestamp after successful train execution.
/// </summary>
internal class SaveDatabaseChangesJunction(IDataContext dataContext) : EffectJunction<Unit, Unit>
{
    public override async Task<Unit> Run(Unit input)
    {
        await dataContext.SaveChanges(CancellationToken);

        return Unit.Default;
    }
}
