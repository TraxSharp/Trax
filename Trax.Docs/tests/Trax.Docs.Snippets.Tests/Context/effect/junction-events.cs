// What the compiled examples on effect/junction-events.md take as given.
global using static JunctionEventsPage;
using Trax.Effect.Data.Services.DataContext;

public static class JunctionEventsPage
{
    public static IDataContext dataContext => throw new NotSupportedException();

    public static long metadataId => 1;
}
