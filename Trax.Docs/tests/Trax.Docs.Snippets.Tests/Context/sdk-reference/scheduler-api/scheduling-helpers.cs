// What the ManifestOptions example on sdk-reference/scheduler-api/scheduling-helpers.md takes as
// given: a runtime scheduler to call.
global using static SchedulingHelpersHost;

public static class SchedulingHelpersHost
{
    public static ITraxScheduler scheduler => serviceProvider.GetRequiredService<ITraxScheduler>();
}
