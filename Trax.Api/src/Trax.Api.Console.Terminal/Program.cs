using Trax.Api.Console.Terminal;

return await ConsoleCommandLine
    .Create(clock: TimeProvider.System)
    .Parse(args)
    .InvokeAsync()
    .ConfigureAwait(false);
