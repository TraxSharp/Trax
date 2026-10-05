using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Hub.Trains.HelloWorld.Junctions;

namespace Trax.Samples.Hub.Trains.HelloWorld;

/// <summary>
/// A train that logs a greeting. [TraxMutation] exposes it as
/// mutation { dispatch { helloWorld(input: { name: "..." }) { externalId metadataId } } }.
/// Program.cs also schedules it every 20 seconds.
/// <para>
/// GraphQLOperation.Run means the mutation runs the train and answers when it has finished.
/// [TraxMutation] with no operation also adds a mode: QUEUE argument, which hands the run to the
/// scheduler instead. Only a database-backed scheduler (UsePostgres) reads that queue: on the
/// in-memory provider a queued run is never dispatched, so this template exposes Run alone.
/// </para>
/// <para>
/// [TraxAuthorize] is required: the host refuses to start when an exposed train declares neither
/// it nor [TraxAllowAnonymous]. Callers need the User role, which the demo key carries in
/// Development (see Program.cs).
/// </para>
/// </summary>
[TraxAuthorize(Roles = "User")]
[TraxMutation(GraphQLOperation.Run, Description = "Runs a hello world greeting")]
public class HelloWorldTrain : ServiceTrain<HelloWorldInput, Unit>, IHelloWorldTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<LogGreetingJunction>().Resolve();
}
