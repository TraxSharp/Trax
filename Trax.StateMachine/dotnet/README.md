# Trax.StateMachine

[![Build](https://github.com/TraxSharp/Trax/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/TraxSharp/Trax/actions/workflows/release.yml?query=branch%3Amain)
[![NuGet](https://img.shields.io/nuget/v/Trax.StateMachine)](https://www.nuget.org/packages/Trax.StateMachine)
[![codecov](https://codecov.io/gh/TraxSharp/Trax/branch/main/graph/badge.svg?flag=Trax.StateMachine)](https://app.codecov.io/gh/TraxSharp/Trax?flags%5B0%5D=Trax.StateMachine)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax/blob/main/Trax.StateMachine/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/statemachine)

> Part of [Trax .NET](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [Repository](https://github.com/TraxSharp/Trax)

Trax.StateMachine is the portable state-machine engine: a multi-step flow's state as language-neutral JSON, with
each event's guard, reducer and caller rule declared as data. `Trax.StateMachine.Persistence` stores each
instance per user, runs a machine's one irreversible effect exactly once, and lets a state invoke a train.
`@traxsharp/state-machine` on npm runs the same machine in the browser.

## Install

```bash
dotnet add package Trax.StateMachine.Persistence
dotnet add package Trax.StateMachine.Testing   # in a test project
dotnet add package Trax.StateMachine.Client    # in a .NET frontend that drives a served machine
```

## Example

From [Author and serve a machine](https://traxsharp.net/docs/statemachine/authoring):

```csharp
[TraxMachine]
public sealed class CheckoutMachine : Machine<CheckoutState, CheckoutTrigger>
{
    protected override void Configure(IMachineBuilder<CheckoutState, CheckoutTrigger> m)
    {
        m.Id("checkout").Version(1) /* start, rules on Get, Watch and Discard */;

        m.In(CheckoutState.Review)
            .Context<CartContext>()
            .On(CheckoutTrigger.Pay)
            .AnyAuthenticated()
            .WithInput<PayInput>()
            .RunsOnce<ICharge>("checkout:charge")
            .To(CheckoutState.Paid);

        m.In(CheckoutState.Paid).Context<PaidContext>().Committed();
    }
}

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(connectionString))
        .AddStateMachines(typeof(CheckoutMachine).Assembly)
        .AddMediator(typeof(CheckoutMachine).Assembly));
```

The server runs `ICharge` once per send and fires `Pay` with its receipt. A client never fires `Pay` itself.

## Where this fits

Trax is split into layers, one folder of this repository each. Take the ones you need; the trains you wrote do not change. **You are here: Trax.StateMachine.**

| Folder | What it adds |
|---|---|
| [Trax.Core](https://github.com/TraxSharp/Trax/tree/main/Trax.Core) | Trains, junctions and the chain, with no database and no DI container |
| [Trax.Effect](https://github.com/TraxSharp/Trax/tree/main/Trax.Effect) | A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers |
| **[Trax.StateMachine](https://github.com/TraxSharp/Trax/tree/main/Trax.StateMachine)** | **State machines a page drives and the server applies, in C# and TypeScript, held to one conformance kit** |
| [Trax.Mediator](https://github.com/TraxSharp/Trax/tree/main/Trax.Mediator) | The train bus: run a train by handing over its input, with every chain checked at startup |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax/tree/main/Trax.Scheduler) | Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda |
| [Trax.Api](https://github.com/TraxSharp/Trax/tree/main/Trax.Api) | GraphQL generated from your trains, with authentication, audit and typed clients, and the operations console your host serves |
| [Trax.Cli](https://github.com/TraxSharp/Trax/tree/main/Trax.Cli) | The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen |
| [Trax.Samples](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples) | Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates |

Docs live in [Trax.Docs](https://github.com/TraxSharp/Trax/tree/main/Trax.Docs) and are published at [traxsharp.net/docs](https://traxsharp.net/docs).

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax/blob/main/Trax.StateMachine/AGENTS.md) before changing code. Report
vulnerabilities privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax/blob/main/Trax.StateMachine/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax .NET is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
