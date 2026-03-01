using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Services.EffectStep;

namespace Trax.Scheduler.Workflows.TaskServerExecutor.Steps;

/// <summary>
/// Updates the Manifest's LastSuccessfulRun timestamp after successful workflow execution.
/// </summary>
internal class SaveDatabaseChangesStep(IDataContext dataContext) : EffectStep<Unit, Unit>
{
    public override async Task<Unit> Run(Unit input)
    {
        await dataContext.SaveChanges(CancellationToken);

        return Unit.Default;
    }
}
