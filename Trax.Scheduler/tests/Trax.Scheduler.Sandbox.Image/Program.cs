using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Sandbox.Image;

var services = new ServiceCollection()
    .AddLogging(logging => logging.AddSimpleConsole())
    .AddTrax(trax =>
        trax.AddEffects(effects => effects.UseInMemory()).AddMediator(typeof(HostileTrain).Assembly)
    )
    .AddTraxSandboxRunner()
    .BuildServiceProvider();

await using (services.ConfigureAwait(false))
    return await services.RunTraxSandboxAsync().ConfigureAwait(false);
