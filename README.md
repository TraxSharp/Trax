<p align="center">
  <img src="Trax.Website/src/app/icon.png" width="96" height="96" alt="Trax .NET">
</p>

<h1 align="center">Trax .NET</h1>

<p align="center">
  <b>The backend that checks the client's work.</b><br>
  Workflows the server enforces, whoever drives them: a browser, a script or an AI agent.
</p>

<p align="center">
  <a href="#quick-start">Quick start</a> ·
  <a href="https://traxsharp.net/docs/introduction">Trax in five minutes</a> ·
  <a href="https://traxsharp.net/docs">Docs</a> ·
  <a href="https://traxsharp.net/docs/book">The book</a> ·
  <a href="Trax.Samples">Samples</a>
</p>

<p align="center">
  <a href="https://github.com/TraxSharp/Trax/actions/workflows/ci.yml"><img src="https://github.com/TraxSharp/Trax/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://www.nuget.org/packages?q=Trax"><img src="https://img.shields.io/nuget/v/Trax.Effect" alt="NuGet"></a>
  <a href="https://www.npmjs.com/package/@traxsharp/state-machine"><img src="https://img.shields.io/npm/v/@traxsharp/state-machine" alt="npm"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License: MIT"></a>
</p>

<p align="center">
  <img src="Trax.Docs/images/propose-decide.svg" width="768" alt="Outside Trax, a browser, a script and an AI agent each send a move, never the new state. Inside Trax, the move arrives through the API, which knows who is calling, and Trax checks it against the stored state: is the move allowed from here, may this caller make it, and do the data and evidence hold up. The last two run rules you write. All three pass and Trax saves the new state and runs the work you wrote; any one fails and the client gets a code such as GUARD_FAILED with your reason, and nothing changes.">
</p>

Trax is a set of NuGet packages for your ASP.NET Core app. You write a process once in C#: the
states it can be in, the moves between them, who may make each move, and the work each step does.
Trax serves it over GraphQL, checks every move on the server against the state it stored, and
records every run in your own Postgres or SQLite database. A client sends a move, never the
result, so a page that skips a step, a script that sends the wrong shape, or an agent that approves
its own work gets a typed refusal instead of a corrupted order.

## Quick start

```bash
dotnet new install Trax.Samples.Templates
dotnet new trax-hub -n MyApp      # or trax-api, trax-scheduler
cd MyApp && dotnet run            # http://localhost:5400/trax/graphql and /trax/dashboard
dotnet test tests/MyApp.Tests
```

`trax-hub` gives you a GraphQL API, a scheduler and the operations console in one process, with
runs stored in memory so it needs no database. Every Trax package releases at one version, so
install the same version of each one you take.

To build one up from an empty folder instead, follow the
[Quick start](https://traxsharp.net/docs/getting-started). To learn it properly, [the
book](https://traxsharp.net/docs/book) builds a shop one feature per chapter.

## What you write, and what Trax does

| You write | Trax does |
|---|---|
| The states, and the moves allowed from each | Stores the state, and refuses any move you did not list |
| Who may send each move | Reads the caller from your app's sign-in, and refuses anyone your rule leaves out |
| The guards, and the reason each one gives back | Runs them against the stored state, and returns your reason when one fails |
| The trains: the work, as small steps called junctions | Runs them in order, stops at the first failure, and records every run |
| A few lines in `Program.cs` that register Trax | Serves your machines and trains over GraphQL, and generates a typed TypeScript client |
| When background work runs, and how often to retry it | Queues it, retries it, dead-letters it, and shows it in the operations console |

## Example

A checkout, from [the book](https://traxsharp.net/docs/book). The rules are a state machine; this
is part of it:

```csharp
m.In(CheckoutState.Address)
    .On(CheckoutTrigger.ChoosePayment)
    .AnyAuthenticated()
    .WithInput<PaymentInput>()
    .When(Input((PaymentInput i) => i.PaymentToken).NonEmpty())
    .Because("Choose how to pay.")
    .To(CheckoutState.Payment);

m.In(CheckoutState.Payment)
    .On(CheckoutTrigger.PlaceOrder)
    .Requires(Roles.Customer)
    .To(CheckoutState.Placing);

m.In(CheckoutState.Placing)
    .Invokes<IPlaceOrderTrain, PlaceOrderInput, PlaceOrderOutput>(OrderFor)
    .OnDone(CheckoutState.Placed)
    .OnFailed(CheckoutState.Failed);
```

The work is a train, a chain of junctions. Each junction is a small class you write; a junction
that throws stops the chain, so no card is charged for an order that cannot be filled:

```csharp
public class PlaceOrderTrain : ServiceTrain<PlaceOrderInput, PlaceOrderOutput>, IPlaceOrderTrain
{
    protected override Task<Either<Exception, PlaceOrderOutput>> Junctions() =>
        Chain<PriceCartJunction>()
            .Chain<ReserveStockJunction>()
            .Chain<TakePaymentJunction>()
            .Chain<ConfirmOrderJunction>()
            .Resolve();
}
```

From those, Trax serves `ChoosePayment` and `PlaceOrder` as GraphQL mutations and stores each
checkout's state. A client that sends `PlaceOrder` from the Address state gets `NO_TRANSITION`; a
caller who is not a customer gets `FORBIDDEN`; an empty payment token gets `GUARD_FAILED` with
"Choose how to pay.". When the checkout reaches Placing, Trax starts the train and moves the
checkout to Placed or Failed from its result. Both snippets are trimmed; the full machine is in
[`CheckoutMachine.cs`](Trax.Samples/book/Shopfront/Shopfront.Core/Machines/Checkout/CheckoutMachine.cs).

## Features

- **State machines the server applies.** States, moves, roles and guards in C#. The client sends
  a move and the server works out the result. A TypeScript twin,
  [`@traxsharp/state-machine`](https://www.npmjs.com/package/@traxsharp/state-machine), runs the
  same rules in the browser so a page can show what is allowed before it asks.
- **Trains.** One operation as typed steps. Each step's output reaches later steps by its type,
  with no wiring code, and a host whose chain can never run refuses to start.
- **Closed by default.** Nothing is served until a rule says who may call it, and an unguarded
  train stops the host at startup. Turn on persisted operations and the server runs only the
  GraphQL documents you registered.
- **GraphQL and a generated client.** Trains and machines become typed mutations and queries. The
  `trax` tool generates a typed TypeScript client from the documents your frontend writes.
- **Every run recorded.** Input, output, timing, and the step and exception that failed it, as
  rows in your own database. A run can save a checkpoint and resume from it after a crash.
- **Schedules and workers.** Cron, intervals, one-off delays and dependent runs, with retries,
  backoff and dead letters. Run the same trains on workers in other processes, over HTTP, SQS or
  AWS Lambda.
- **The operations console.** Every run, schedule, dead letter and machine in one place, with
  requeue and cancel, served by your host.
- **AI agents as clients.** An agent gets only the moves you declared for its role, and the
  evidence it submits is rerun by a check train you write before anything advances.

[What Trax does](https://traxsharp.net/docs/what-trax-does) lists every capability, with the book
chapter that teaches it and the package that ships it.

## Samples

<table>
  <tr>
    <td width="50%"><a href="Trax.Samples/samples/StateMachine"><img src="Trax.Samples/samples/StateMachine/screenshot.png" alt="The StateMachine sample: a checkout machine in the browser, the triggers a user can fire, and a panel listing what the server just did."></a></td>
    <td width="50%"><a href="Trax.Samples/samples/Recovery"><img src="Trax.Samples/samples/Recovery/screenshot.png" alt="The Recovery sample: a refund train's code, the model's recorded answers, and a timeline where the first attempt crashes and the second resumes from a checkpoint."></a></td>
  </tr>
  <tr>
    <td><a href="Trax.Samples/samples/StateMachine"><b>StateMachine</b></a>: a checkout the browser drives and the server decides, with a charge that runs exactly once.</td>
    <td><a href="Trax.Samples/samples/Recovery"><b>Recovery</b></a>: a crashed run resumes without asking its model the same questions twice.</td>
  </tr>
</table>

- [**Board**](Trax.Samples/samples/Board): agents and a person move bug tickets through lanes, and
  only a server-side check moves a ticket.
- [**ChatService**](Trax.Samples/samples/ChatService): a chat server whose messages are trains,
  pushed to rooms over GraphQL subscriptions.
- [**Scheduling**](Trax.Samples/samples/Scheduling): cron and interval schedules, retries, and a
  dead letter to requeue.
- [**DistributedWorkers**](Trax.Samples/samples/DistributedWorkers): one process schedules, others
  run the work, sharing only Postgres and RabbitMQ.

Every sample is in [Trax.Samples](Trax.Samples), with how to run it.

## Where this fits

Trax is split into layers, one folder each, and every package releases at one version from this repository. Each .NET
folder depends only on folders above it in this table; take the layers you need, and the trains you wrote do not change.

| Folder | What it adds |
|---|---|
| [Trax.Core](Trax.Core) | Trains, junctions and the chain, with no database and no DI container |
| [Trax.Effect](Trax.Effect) | A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers |
| [Trax.StateMachine](Trax.StateMachine) | State machines a page drives and the server applies, in C# and TypeScript, held to one conformance kit |
| [Trax.Mediator](Trax.Mediator) | The train bus: run a train by handing over its input, with every chain checked at startup |
| [Trax.Scheduler](Trax.Scheduler) | Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda |
| [Trax.Api](Trax.Api) | GraphQL generated from your trains and machines: a mutation per machine event, authorization checked on the server, persisted operations, audit, live subscriptions and the operations console your host serves |
| [Trax.Cli](Trax.Cli) | The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen |
| [Trax.Samples](Trax.Samples) | Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates |
| [Trax.Docs](Trax.Docs) | The documentation published at [traxsharp.net/docs](https://traxsharp.net/docs), and the decision records |
| [Trax.Website](Trax.Website) | The source of [traxsharp.net](https://traxsharp.net) |

Trax used to be split into one repository per layer (`TraxSharp/Trax.Core` and the rest). Those are archived; their
history is here, under each folder.

<details>
<summary><b>The coloured squares in release notes and pull requests</b></summary>

Release notes, pull requests and the docs mark each change with the parts of Trax it touches. The parts are concepts,
not folders: `Trax.Effect` alone holds data, plumbing and trains. A change that crosses several shows
several squares, innermost first.

| | Part | What it covers |
|---|---|---|
| 🟨 | Data | The run-record tables, migrations and the Postgres, SQLite and in-memory providers |
| 🟫 | Plumbing | Effect and junction providers, broadcasters (RabbitMQ, SignalR), lifecycle hooks |
| 🟩 | Trains | Trains, junctions, chains, decisions, `Parallel` |
| 🟪 | Machines | State machines, in .NET and in `@traxsharp/state-machine` |
| 🟧 | Dispatch | The train bus, authorization, the scheduler (manifests, retries, dead letters), remote workers and Lambda |
| 🟦 | Surface | Trax.Api: GraphQL, auth, persisted operations, generated clients, the operations console |
| ⬛ | Tooling | The `trax` CLI, analyzers, architecture guards, CI, releases, the docs |
| 🟥 | Security | Not a part: a flag on any change to authentication, authorization or what is exposed |

A release note headed 🟧🟦🟥, for example, changes how work is dispatched and what the API serves, and it is worth
reading for its security impact before you upgrade.

</details>

## Sponsors

<a href="https://constituentvoice.com/"><img src="https://github.com/constituentvoice.png" width="64" alt="Constituent Voice"></a>

Trax is sponsored by [Constituent Voice](https://constituentvoice.com/). To support it too, see
[GitHub Sponsors](https://github.com/sponsors/TraxSharp).

## Contributing

Read [AGENTS.md](AGENTS.md) before changing anything, then the `AGENTS.md` in the folder you are working in. Report
vulnerabilities privately as described in [SECURITY.md](SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
