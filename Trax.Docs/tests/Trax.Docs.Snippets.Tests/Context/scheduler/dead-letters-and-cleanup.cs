// What the examples on scheduler/dead-letters-and-cleanup.md take as given: a scheduler host's
// usings, the data context they read and the ids they act on.
global using static DeadLetterDocsHost;
global using Microsoft.EntityFrameworkCore;
global using Trax.Effect.Data.Postgres.Extensions;
global using Trax.Effect.Enums;
global using Trax.Effect.Extensions;
global using Trax.Mediator.Extensions;
global using Trax.Scheduler.Extensions;
using Trax.Effect.Data.Services.DataContext;

public static class DeadLetterDocsHost
{
    public static IDataContext context => serviceProvider.GetRequiredService<IDataContext>();

    public static long manifestId => 42;

    public static long deadLetterId => 42;
}
