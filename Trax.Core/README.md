# Trax.Core

[![Build](https://github.com/TraxSharp/Trax.Core/actions/workflows/nuget_release.yml/badge.svg?branch=main)](https://github.com/TraxSharp/Trax.Core/actions/workflows/nuget_release.yml?query=branch%3Amain)
[![NuGet](https://img.shields.io/nuget/v/Trax.Core)](https://www.nuget.org/packages/Trax.Core)
[![codecov](https://codecov.io/gh/TraxSharp/Trax.Core/branch/main/graph/badge.svg)](https://codecov.io/gh/TraxSharp/Trax.Core)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax.Core/blob/main/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/core)

> Part of [Trax](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [All repos](https://github.com/TraxSharp)

Trax.Core defines trains for .NET. A train is a typed pipeline of small steps (junctions): when a junction throws, the rest are skipped and the train returns the exception. It has no database and no DI container, and the other Trax layers build on it.

## Install

```bash
dotnet add package Trax.Core
dotnet add package Trax.Core.Testing   # optional: architecture-guard fixtures for your NUnit tests
```

Trax.Core targets .NET 10.

## Example

```csharp
using LanguageExt;
using Trax.Core.Junction;
using Trax.Core.Train;

public record CreateUserRequest(string Email);
public record User(Guid Id, string Email);

public class ValidateEmailJunction : Junction<CreateUserRequest, Unit>
{
    public override Task<Unit> Run(CreateUserRequest input)
    {
        if (!input.Email.Contains('@'))
            throw new ArgumentException($"'{input.Email}' is not an email address");
        return Task.FromResult(Unit.Default);
    }
}

public class CreateUserJunction : Junction<CreateUserRequest, User>
{
    public override Task<User> Run(CreateUserRequest input) =>
        Task.FromResult(new User(Guid.NewGuid(), input.Email));
}

public class CreateUserTrain : Train<CreateUserRequest, User>
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<ValidateEmailJunction>()
            .Chain<CreateUserJunction>()
            .Resolve();
}
```

Run it with `RunEither`, which never throws, and read the `Either`:

```csharp
Either<Exception, User> result = await new CreateUserTrain().RunEither(new CreateUserRequest("ada@example.com"));

string message = result.Match(
    Right: user => $"created {user.Id}",
    Left: ex => $"failed: {ex.Message}");
```

`Run` returns the `User` directly and rethrows the exception instead. Each junction's output is stored in Memory under its type, and the next junction asks for its input by type, so values are never passed between junctions by hand. If `ValidateEmailJunction` throws, `CreateUserJunction` does not run and `result` holds the `ArgumentException`.

A junction that needs a service takes it in its constructor. In a plain `Train` you put the service in Memory with `AddServices(repository)` ahead of the first `Chain`. A `ServiceTrain` from Trax.Effect resolves it from dependency injection instead.

## Chain checking

A chain is a declaration, so it can be checked without running it. With Trax.Mediator registered, the host reads every train's `Junctions()` at startup and refuses to start if a junction asks for a type nothing earlier provides, or the chain ends without the train's output. Trax.Core alone does not run that check.

`Trax.Core.Analyzers` is deprecated and reports nothing. It only read chains rooted at `Activate()`, which can no longer be written, so there is no reason to install it.

## Packages

| Package | What it adds |
|---|---|
| [Trax.Core](https://www.nuget.org/packages/Trax.Core) | `Train`, `Junction`, the chain and Memory |
| [Trax.Core.Testing](https://www.nuget.org/packages/Trax.Core.Testing) | Architecture-guard base fixtures and hygiene checks for NUnit; the checkers also return offender lists for other test frameworks |
| [Trax.Core.Analyzers](https://www.nuget.org/packages/Trax.Core.Analyzers) | Deprecated, reports nothing. Do not install it |

## What it does not do

- It records nothing. A run that should leave a record is a `ServiceTrain` from Trax.Effect.
- It has no DI container. Junction dependencies come from Memory or `AddServices`.
- It does not check chains before they run. That check comes with Trax.Mediator.

## Where this fits

Trax is split into layers, one repo each. Take the ones you need; the trains you wrote do not change. **You are here: Trax.Core.**

| Repo | What it adds |
|---|---|
| **[Trax.Core](https://github.com/TraxSharp/Trax.Core)** | **Trains, junctions and the chain, with no database and no DI container** |
| [Trax.Effect](https://github.com/TraxSharp/Trax.Effect) | A recorded run for every execution (Postgres, SQLite or in memory), DI, effect providers, the state-machine engine |
| [Trax.Mediator](https://github.com/TraxSharp/Trax.Mediator) | The train bus: run a train by handing over its input, with every chain checked at startup |
| [Trax.Scheduler](https://github.com/TraxSharp/Trax.Scheduler) | Cron and interval schedules, retries, dead letters, and workers on other machines or in Lambda |
| [Trax.Api](https://github.com/TraxSharp/Trax.Api) | GraphQL generated from your trains, with authentication, audit and typed clients |
| [Trax.Dashboard](https://github.com/TraxSharp/Trax.Dashboard) | A Blazor Server UI for runs, schedules and dead letters, mounted in your app |
| [Trax.Cli](https://github.com/TraxSharp/Trax.Cli) | The `trax` tool: scaffold a hub and trains from an OpenAPI or GraphQL schema, and state-machine codegen |
| [Trax.Samples](https://github.com/TraxSharp/Trax.Samples) | Complete sample apps, and the `trax-api`, `trax-scheduler` and `trax-hub` templates |

Docs live in [Trax.Docs](https://github.com/TraxSharp/Trax.Docs) and are published at [traxsharp.net/docs](https://traxsharp.net/docs).

## Documentation

- [Core overview](https://traxsharp.net/docs/core)
- [Trains and junctions](https://traxsharp.net/docs/core/trains-and-junctions)
- [Building chains](https://traxsharp.net/docs/core/building-chains)
- [Memory](https://traxsharp.net/docs/core/memory)
- [Architecture guards](https://traxsharp.net/docs/reference/architecture-guards)

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax.Core/blob/main/AGENTS.md) before changing code. Report vulnerabilities
privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax.Core/blob/main/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
