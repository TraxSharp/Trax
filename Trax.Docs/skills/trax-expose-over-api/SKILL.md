---
name: trax-expose-over-api
description: Use when exposing Trax trains, query models or state machines over GraphQL, or setting up who may call them. Covers AddTraxGraphQL and UseTraxGraphQL, [TraxQuery], [TraxMutation], [TraxQueryModel], [TraxMachine], the required [TraxAuthorize] or [TraxAllowAnonymous] posture, API key, JWT and OIDC auth, AddTraxPrincipalAccessor and injecting TraxPrincipal, persisted operations and client documents, Origin and CORS for browser clients and WebSocket subscriptions, error masking and the TRAX_* error codes a caller sees, and registration order. Also use when a GraphQL call is refused with TRAX_AUTHORIZATION, PERSISTED_OPERATION_REQUIRED or Unexpected Execution Error, or a browser socket gets a 403.
metadata:
  internal: true
---

# Expose trains and machines over the API

`Trax.Api.GraphQL` serves a host's trains, query models and machines as one GraphQL endpoint,
`/trax/graphql` by default. It is closed by design: every surface declares who may reach it or the
host refuses to start, a caller with no credential reaches only what you marked public, and a
refusal says nothing about what exists. Treat each closed default as the requirement. If something
is refused, find out why; never open a default to make a call work.
[The GraphQL API](https://traxsharp.net/docs/api) explains the model, and
[Secure a GraphQL host](https://traxsharp.net/docs/security/secure-a-graphql-host) is the whole
setup in one `Program.cs`. Read that page before writing the host.

## Decide first

1. **What each surface is.** Business logic is a train: `[TraxQuery]` (under `discover`) or
   `[TraxMutation]` (under `dispatch`). A table read the caller filters and pages is a query model,
   `[TraxQueryModel]` on an EF Core entity. A process across requests is a machine, `[TraxMachine]`.
   A train with none of these is not on the API.
   [Query models and query trains](https://traxsharp.net/docs/api/query-models-and-query-trains)
   compares the two reads.
2. **Who may call each one.** `[TraxAuthorize]` (any signed-in caller, or a role or policy) or
   `[TraxAllowAnonymous]`. Default to `[TraxAuthorize]`. A public surface is the user's decision:
   ask, and say that anyone on the network can then call it.
3. **How callers sign in.** API keys for services and scripts (`Trax.Api.Auth.ApiKey`), JWTs from
   an identity provider (`Trax.Api.Auth.Jwt`), interactive browser sign-in with a cookie
   (`Trax.Api.Auth.Oidc`). Which provider, issuer and audience production uses is the user's to
   say; never invent production credentials.
4. **Run or queue.** `GraphQLOperation.Run` answers with the output. `Queue` writes a work queue
   entry for a scheduler, which needs a database the scheduler shares; on the in-memory provider a
   queue is refused. See [Queue or run](https://traxsharp.net/docs/api#queue-or-run).
5. **Persisted operations.** Required to expose any machine, and the way a production frontend
   ships: the host runs only documents it registered. Ask the user before turning them off for a
   host that has them.

## Steps

1. **Packages.** `Trax.Api.GraphQL` plus one or more of `Trax.Api.Auth.ApiKey`, `.Jwt`, `.Oidc`,
   all at the version the other `Trax.*` packages have. The namespaces agents get wrong:

   | Type or call | Namespace |
   |---|---|
   | `AddTraxGraphQL`, `UseTraxGraphQL` | `Trax.Api.GraphQL.Extensions` |
   | `AddTraxPrincipalAccessor`, `TraxPrincipal`, `TraxCaller` | `Trax.Api.Auth` |
   | `AddTraxApiKeyAuth` / `AddTraxJwtAuth` / `AddTraxOidcAuth` | `Trax.Api.Auth.ApiKey` / `.Jwt` / `.Oidc` |
   | `[TraxQuery]`, `[TraxMutation]`, `GraphQLOperation`, `[TraxQueryModel]`, `[TraxMachine]`, `[TraxAuthorize]`, `[TraxAllowAnonymous]` | `Trax.Effect.Attributes` |
   | `TrainException` | `Trax.Core.Exceptions` |
   | `UsePersistedOperations` | `Trax.Api.GraphQL.PersistedOperations.Extensions` |

2. **Write the host.** Demo credentials exist only inside `IsDevelopment()` and carry
   `do-not-use-in-production`; the host refuses to start with one registered anywhere else. Where the
   production credentials come from (hashed keys from a secret store, an authority) is the user's
   call; [Secure a GraphQL host](https://traxsharp.net/docs/security/secure-a-graphql-host#1-write-the-host)
   shows both branches.

   ```csharp compile=api
   using Trax.Api.Auth;
   using Trax.Api.Auth.ApiKey;
   using Trax.Api.GraphQL.Extensions;
   using Trax.Effect.Data.Postgres.Extensions;
   using Trax.Effect.Extensions;
   using Trax.Mediator.Extensions;

   var builder = WebApplication.CreateBuilder(args);

   if (builder.Environment.IsDevelopment())
       builder.Services.AddTraxApiKeyAuth(keys =>
           keys.Add("member-key-do-not-use-in-production", id: "member", "Member"));
   // Production credentials go here, from a secret store. Ask the user which.

   builder.Services.AddAuthentication();        // the auth call above is conditional,
   builder.Services.AddTraxPrincipalAccessor(); // so register what it would bring
   builder.Services.AddAuthorization();         // and every policy a gate names

   builder.Services.AddTrax(trax => trax
       .AddEffects(effects => effects.UsePostgres(builder.Configuration.GetConnectionString("Trax")!))
       .AddMediator(typeof(Program).Assembly));

   builder.Services.AddTraxGraphQL(); // after AddTrax, which registered the trains

   var app = builder.Build();
   app.UseAuthentication();
   app.UseAuthorization();
   app.UseTraxGraphQL();
   app.Run();
   ```

3. **Expose a train and give it a posture.** The junction reads the caller from DI. A train gated
   by `[TraxAuthorize]` refuses an anonymous caller before its junctions are built, so `TraxPrincipal`
   is always there. Its `Id` is scheme-qualified (`TraxApiKey:member`); key ownership on it as is.

   ```csharp compile=api
   using Trax.Api.Auth;
   using Trax.Core.Exceptions;
   using Trax.Core.Functional;
   using Trax.Core.Junction;
   using Trax.Effect.Attributes;
   using Trax.Effect.Services.ServiceTrain;

   public record RenameNoteInput(string NoteId, string Title);

   public record RenamedNote(string NoteId, string Title, string RenamedBy);

   public interface IRenameNoteTrain : IServiceTrain<RenameNoteInput, RenamedNote>;

   [TraxAuthorize(Roles = "Member")]
   [TraxMutation(GraphQLOperation.Run)]
   public class RenameNoteTrain : ServiceTrain<RenameNoteInput, RenamedNote>, IRenameNoteTrain
   {
       protected override Task<Either<Exception, RenamedNote>> Junctions() =>
           Chain<RenameNoteJunction>().Resolve();
   }

   internal class RenameNoteJunction(TraxPrincipal user) : Junction<RenameNoteInput, RenamedNote>
   {
       public override Task<RenamedNote> Run(RenameNoteInput input) =>
           string.IsNullOrWhiteSpace(input.Title)
               ? throw new TrainException("A note needs a title.")
               : Task.FromResult(new RenamedNote(input.NoteId, input.Title, user.Id));
   }
   ```

   Field names, namespaces and input rules are on
   [TraxQuery and TraxMutation](https://traxsharp.net/docs/sdk-reference/graphql-api/trax-graphql-attribute).
4. **Query models.** `[TraxQueryModel]` and a posture on the entity, the entity in a `DbSet` on its
   context, and `AddTraxGraphQL(graphql => graphql.AddDbContext<TContext>())`. Every entity it reaches
   through a navigation needs a posture too. `[TraxAuthorize]` decides who may read the type, not
   which rows: scope rows with a query filter over the caller. See
   [Expose an entity as a query model](https://traxsharp.net/docs/api/expose-an-entity-as-a-query-model)
   and [What the attributes do not cover](https://traxsharp.net/docs/security/authorization-rules#what-the-attributes-do-not-cover).
5. **Machines.** `[TraxMachine]` on the concrete machine is the only switch. Every event, the start,
   the read and the discard need a rule, and the host must call `UsePersistedOperations`, or it
   refuses to start. Design the machine with `trax-design-a-machine`; the served operations are on
   [Machines root](https://traxsharp.net/docs/sdk-reference/graphql-api/machines).
6. **Live updates.** A train callers watch over a subscription carries `[TraxBroadcast]`. To let only
   the caller who started a run follow it, use `[TraxRunProgress]`
   ([Run progress](https://traxsharp.net/docs/sdk-reference/graphql-api/run-progress)).
7. **Persisted operations and client documents.** A frontend writes `.graphql` documents,
   `trax client --documents` turns them into `trax.documents.json`, and the host registers them at
   boot with `UsePersistedOperations(po => po.SingleNode().ClientDocuments("<path>"))`, which requires
   persisted documents by default. `SingleNode()` claims one process serves the endpoint; with more,
   it is `UseRabbitMqInvalidation(...)`. Steps are in
   [Fetch only what a component needs](https://traxsharp.net/docs/api/fetch-only-what-a-component-needs)
   and [Turn on persisted operations](https://traxsharp.net/docs/api/turn-on-persisted-operations);
   the client side is `trax-generate-a-client`.
8. **Browser clients.** Browsers send `Origin`; Node clients and curl do not, so a Node test that
   passes says nothing about the browser. A socket upgrade with an `Origin` is accepted from the
   endpoint's own host, or from the origins your CORS default policy names, or those set with
   `AllowSocketOrigins(...)`. Which production origins to allow is the user's call: ask. HTTP CORS is
   the host's own ASP.NET Core policy. A browser sends its socket credential in `connection_init`
   (`authToken`), not as a header. See
   [Allowed origins](https://traxsharp.net/docs/sdk-reference/graphql-api/subscriptions#allowed-origins)
   and [Authentication](https://traxsharp.net/docs/sdk-reference/graphql-api/subscriptions#authentication).
9. **Start it and call it without a credential.** A missing posture, an unregistered policy or a
   misplaced demo key stops the host with a message naming the fix
   ([Startup refusals](https://traxsharp.net/docs/security/startup-refusals)). An anonymous call to a
   gated train must answer `TRAX_AUTHORIZATION`; test that as well as the allowed call.

## What a caller sees

Error messages are masked on purpose. A caller branches on `extensions.code`, never on the
message. A plain `TrainException`'s message passes through as `TRAX_TRAIN_ERROR`; a subclass of it
shows `The train failed.`; any other exception, `InvalidOperationException` included, shows
`Unexpected Execution Error`. So a message the caller should read is a plain
`new TrainException("...")`, never a type of your own. Every authorization refusal is `TRAX_AUTHORIZATION` with
`Not authorized.`, naming no train, role or policy. The full list is on
[Error codes](https://traxsharp.net/docs/sdk-reference/graphql-api/error-codes). To see what really
failed, read the server log and the run's metadata row (`trax-troubleshoot`). Do not switch on
exception details in the response to read it.

## Known mistakes

- **HotChocolate's attributes.** Trax refuses them on what it checks, and they mean nothing on a train.

  ```csharp fragment
  [Authorize(Roles = "Member")]       // wrong: HotChocolate's, not read on a train
  [TraxAuthorize(Roles = "Member")]   // right
  ```

- **The principal accessor namespace.** `AddTraxPrincipalAccessor` is `using Trax.Api.Auth;`, not an
  auth package's namespace. Every `AddTrax*Auth` call runs it; call it yourself when those calls are
  conditional, or the host refuses to start outside Development, naming the junction that needs
  `Trax.Api.Auth.TraxPrincipal`. Call `AddAuthentication()` unconditionally for the same reason.
- **`TraxPrincipal` in a junction the scheduler runs.** A queued run, a scheduled run and a train a
  machine invokes have no HTTP caller, and resolving `TraxPrincipal` throws. Carry the initiator in
  the input, inject `TraxCaller` (whose `Principal` may be null), or, in a train a machine invokes,
  inject `InvokedRun` (`Trax.StateMachine.Persistence`) for the instance's owner. Never put a token
  in a machine's context for a train to check. See
  [Junctions the scheduler also runs](https://traxsharp.net/docs/security/inject-the-current-principal#junctions-the-scheduler-also-runs).
- **A train registered after `AddTraxGraphQL()`.** The host refuses to start, naming it. Register the
  trains in `AddTrax(...)` first. Auth calls may come before or after; see
  [Registration order](https://traxsharp.net/docs/sdk-reference/graphql-api/add-trax-graphql#registration-order).
- **Only mutations.** `AddTraxGraphQL() found no GraphQL queries to expose.` A schema needs a query:
  a `[TraxQuery]` train, a query model, or the gated `operations` queries.
- **Gating the whole endpoint on the route.** A convention passed to `UseTraxGraphQL(configure: ...)`
  is enforced, but the startup checks do not see it. Use the builder's `RequireAuthorization()`.
- **Exposing `operations`.** `ExposeOperationQueries()` and `ExposeOperationMutations()` need a gate
  with a role or policy, `GateOperations(roles: "Operator")`, even on an endpoint gated with
  `RequireAuthorization()`; they reveal execution inputs and drive the scheduler. The host also needs `AddScheduler(...)` for them.
- **A text query refused with `PERSISTED_OPERATION_REQUIRED`.** The host is working. Register the
  document as a client document, or use the generated client; ask the user before anything else.
- **A browser socket answered `403` through a dev proxy.** Vite's `changeOrigin: true` makes `Host`
  differ from `Origin`. Leave `changeOrigin` off for the GraphQL path.
- **`X-Api-Key` in `connection_init`.** That is the HTTP header's name. The payload key is
  `authToken` (or `apiKey`); anything else is no credential and the socket is closed with `4403`.

## Ask the user, never decide alone

Any change that makes a surface public, lets the endpoint run documents it did not register, admits
another origin in production, returns exception detail to callers, opens the schema outside
Development, opens the `operations` namespace or the console without a gate, or decides where
production credentials come from. Say what it exposes and to whom, and wait for the answer.
[Where Trax checks authorization](https://traxsharp.net/docs/security/where-authorization-is-checked)
says why each default is closed.
