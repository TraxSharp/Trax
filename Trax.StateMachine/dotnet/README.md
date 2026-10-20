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

This folder sits between Trax.Effect, whose trains a machine invokes and whose tables it is stored in, and
Trax.Mediator, which launches those trains. It holds the C# engine (`dotnet/`), the TypeScript engine (`ts/`) and the
conformance kit both are checked against (`conformance/`). Docs are at
[traxsharp.net/docs/statemachine](https://traxsharp.net/docs/statemachine).

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax/blob/main/Trax.StateMachine/AGENTS.md) before changing code. Report
vulnerabilities privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax/blob/main/Trax.StateMachine/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax .NET is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
