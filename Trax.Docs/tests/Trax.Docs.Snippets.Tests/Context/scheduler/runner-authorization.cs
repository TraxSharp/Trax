// What the examples on scheduler/runner-authorization.md take as given: the usings of a scheduler
// or runner host, the key both sides share and the token a policy-guarded runner expects.
global using static RunnerAuthorizationHost;
global using Trax.Effect.Data.Postgres.Extensions;
global using Trax.Effect.Extensions;
global using Trax.Mediator.Extensions;
global using Trax.Scheduler.Extensions;

public static class RunnerAuthorizationHost
{
    public static byte[] runnerKey =>
        Convert.FromBase64String(configuration["Trax:RunnerSigningKey"]!);

    public static string schedulerToken => configuration["Trax:SchedulerToken"]!;
}
