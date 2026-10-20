using Trax.Effect.StateMachine.Persistence;

namespace Shopfront.Machines;

public static class InvokedRuns
{
    // The machine instance that invoked the current run, or null in a run no machine invoked.
    public static InvokedRun? Current(IServiceProvider services)
    {
        try
        {
            return services.GetService<InvokedRun>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
