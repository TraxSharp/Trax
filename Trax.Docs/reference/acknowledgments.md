---
title: Acknowledgments
description: "The third-party open-source packages Trax depends on, grouped by the Trax package that references each one, with its licence."
type: reference
area: general
nav_order: 9
---

# Acknowledgments

Trax is built on open-source libraries. This page lists the third-party packages each Trax package
references directly, from its project file, with the licence each is published under. Packages they
bring in transitively are not listed. The Trax packages not named here reference only other Trax
packages and the .NET shared framework: `Trax.Api`, `Trax.Api.Auth`, `Trax.Api.Auth.ApiKey`,
`Trax.Api.Auth.Jwt.Cognito`, `Trax.Api.Auth.Jwt.Cognito.Issuer`, `Trax.Api.Auth.Jwt.Testing`,
`Trax.Api.GraphQL.Audit`, `Trax.Api.GraphQL.Client.Typed`, `Trax.Effect.Broadcaster.SignalR`,
`Trax.Effect.Decisions.SystemOne`, `Trax.Effect.JunctionProvider.Logging`,
`Trax.Effect.JunctionProvider.Progress`, `Trax.Effect.Provider.Parameter`,
`Trax.Effect.StateMachine`, `Trax.Effect.StateMachine.Testing` and `Trax.Samples.Templates`.


## Trax.Core

| Package | Licence | Used for |
|---|---|---|
| [Microsoft.Extensions.DependencyInjection.Abstractions](https://github.com/dotnet/runtime) | MIT | DI abstractions for service registration |
| [Microsoft.Extensions.Logging.Abstractions](https://github.com/dotnet/runtime) | MIT | Logging abstractions |

## Trax.Core.Analyzers

| Package | Licence | Used for |
|---|---|---|
| [Microsoft.CodeAnalysis.Analyzers](https://github.com/dotnet/roslyn) | MIT | Rules for writing Roslyn analyzers |

## Trax.Core.Testing

| Package | Licence | Used for |
|---|---|---|
| [Microsoft.CodeAnalysis.CSharp](https://github.com/dotnet/roslyn) | MIT | C# parsing for the source-scanning guards |
| [NUnit](https://github.com/nunit/nunit) | MIT | The test framework the guard fixtures are written for |

## Trax.Effect

| Package | Licence | Used for |
|---|---|---|
| [Microsoft.Extensions.Hosting.Abstractions](https://github.com/dotnet/runtime) | MIT | Hosted services and the host environment |
| [Microsoft.Extensions.Logging.Abstractions](https://github.com/dotnet/runtime) | MIT | Logging abstractions |

## Trax.Effect.Data

| Package | Licence | Used for |
|---|---|---|
| [Microsoft.EntityFrameworkCore](https://github.com/dotnet/efcore) | MIT | ORM for train metadata persistence |
| [Microsoft.EntityFrameworkCore.Relational](https://github.com/dotnet/efcore) | MIT | Relational database support for EF Core |
| [Npgsql](https://github.com/npgsql/npgsql) | PostgreSQL | ADO.NET data provider for PostgreSQL |

## Trax.Effect.Data.Postgres

| Package | Licence | Used for |
|---|---|---|
| [Npgsql.EntityFrameworkCore.PostgreSQL](https://github.com/npgsql/efcore.pg) | PostgreSQL | EF Core provider for PostgreSQL |
| [EFCore.NamingConventions](https://github.com/efcore/EFCore.NamingConventions) | Apache 2.0 | snake_case table and column names |
| [dbup-core](https://github.com/DbUp/DbUp) | MIT | Schema migration runner |
| [dbup-postgresql](https://github.com/DbUp/dbup-postgresql) | MIT | PostgreSQL support for DbUp |

## Trax.Effect.Data.Sqlite

| Package | Licence | Used for |
|---|---|---|
| [Microsoft.EntityFrameworkCore.Sqlite](https://github.com/dotnet/efcore) | MIT | EF Core provider for SQLite |
| [SQLitePCLRaw.bundle_e_sqlite3](https://github.com/ericsink/SQLitePCL.raw) | Apache 2.0 | The bundled native SQLite library |
| [dbup-core](https://github.com/DbUp/DbUp) | MIT | Schema migration runner |
| [dbup-sqlite](https://github.com/DbUp/dbup-sqlite) | MIT | SQLite support for DbUp |

## Trax.Effect.Data.InMemory

| Package | Licence | Used for |
|---|---|---|
| [Microsoft.EntityFrameworkCore.InMemory](https://github.com/dotnet/efcore) | MIT | In-memory database provider for development and testing |

## Trax.Effect.Data.Testing

| Package | Licence | Used for |
|---|---|---|
| [Microsoft.CodeAnalysis.CSharp](https://github.com/dotnet/roslyn) | MIT | C# parsing for the source-scanning guards |
| [Npgsql.EntityFrameworkCore.PostgreSQL](https://github.com/npgsql/efcore.pg) | PostgreSQL | Building a context's model offline, and the database invariants |
| [NUnit](https://github.com/nunit/nunit) | MIT | The test framework the guard fixtures are written for |

## Trax.Effect.Provider.Json

| Package | Licence | Used for |
|---|---|---|
| [Microsoft.Extensions.Logging.Abstractions](https://github.com/dotnet/runtime) | MIT | Logging abstractions |

## Trax.Effect.Broadcaster.RabbitMQ

| Package | Licence | Used for |
|---|---|---|
| [RabbitMQ.Client](https://github.com/rabbitmq/rabbitmq-dotnet-client) | Apache 2.0 or MPL 2.0 | RabbitMQ client for cross-process lifecycle events |

## Trax.Effect.StateMachine.Persistence

| Package | Licence | Used for |
|---|---|---|
| [Microsoft.EntityFrameworkCore](https://github.com/dotnet/efcore) | MIT | Snapshot and draft storage |
| [Microsoft.EntityFrameworkCore.Relational](https://github.com/dotnet/efcore) | MIT | Relational database support for EF Core |
| [Microsoft.Extensions.DependencyInjection](https://github.com/dotnet/runtime) | MIT | Service registration |
| [Microsoft.Extensions.Diagnostics.HealthChecks](https://github.com/dotnet/runtime) | MIT | The state-machine self-check health check |

## Trax.Mediator

| Package | Licence | Used for |
|---|---|---|
| [Microsoft.Extensions.DependencyInjection](https://github.com/dotnet/runtime) | MIT | Service registration and scopes |

## Trax.Mediator.Testing

| Package | Licence | Used for |
|---|---|---|
| [NUnit](https://github.com/nunit/nunit) | MIT | The test framework the guard fixture is written for |

## Trax.Scheduler

| Package | Licence | Used for |
|---|---|---|
| [Cronos](https://github.com/HangfireIO/Cronos) | MIT | Cron expression parser for schedule definitions |

## Trax.Scheduler.Lambda

| Package | Licence | Used for |
|---|---|---|
| [AWSSDK.Lambda](https://github.com/aws/aws-sdk-net) | Apache 2.0 | AWS Lambda client for invoking the runner |

## Trax.Scheduler.Sqs

| Package | Licence | Used for |
|---|---|---|
| [AWSSDK.SQS](https://github.com/aws/aws-sdk-net) | Apache 2.0 | AWS SQS client for distributed job queuing |
| [Amazon.Lambda.SQSEvents](https://github.com/aws/aws-lambda-dotnet) | Apache 2.0 | SQS event types for the Lambda consumer |

## Trax.Runner.Lambda

| Package | Licence | Used for |
|---|---|---|
| [Amazon.Lambda.Core](https://github.com/aws/aws-lambda-dotnet) | Apache 2.0 | AWS Lambda runtime support |
| [Amazon.Lambda.Serialization.SystemTextJson](https://github.com/aws/aws-lambda-dotnet) | Apache 2.0 | Lambda event serialization |

## Trax.Api.Auth.Jwt

| Package | Licence | Used for |
|---|---|---|
| [Microsoft.AspNetCore.Authentication.JwtBearer](https://github.com/dotnet/aspnetcore) | MIT | JWT bearer authentication |

## Trax.Api.Auth.Oidc

| Package | Licence | Used for |
|---|---|---|
| [Microsoft.AspNetCore.Authentication.OpenIdConnect](https://github.com/dotnet/aspnetcore) | MIT | OpenID Connect sign-in |

## Trax.Api.GraphQL

| Package | Licence | Used for |
|---|---|---|
| [HotChocolate.AspNetCore](https://github.com/ChilliCream/graphql-platform) | MIT | GraphQL server for ASP.NET Core |
| [HotChocolate.AspNetCore.Authorization](https://github.com/ChilliCream/graphql-platform) | MIT | Field and type authorization |
| [HotChocolate.CostAnalysis](https://github.com/ChilliCream/graphql-platform) | MIT | Query cost limits |
| [HotChocolate.Data](https://github.com/ChilliCream/graphql-platform) | MIT | Filtering, sorting and paging |
| [HotChocolate.Data.EntityFramework](https://github.com/ChilliCream/graphql-platform) | MIT | EF Core integration for HotChocolate |
| [HotChocolate.Execution.Projections](https://github.com/ChilliCream/graphql-platform) | MIT | Projections into EF queries |

## Trax.Api.GraphQL.PersistedOperations

| Package | Licence | Used for |
|---|---|---|
| [Microsoft.EntityFrameworkCore](https://github.com/dotnet/efcore) | MIT | The persisted operation store |
| [RabbitMQ.Client](https://github.com/rabbitmq/rabbitmq-dotnet-client) | Apache 2.0 or MPL 2.0 | Invalidating cached operations across processes |

## Trax.Api.GraphQL.Testing

| Package | Licence | Used for |
|---|---|---|
| [Microsoft.CodeAnalysis.CSharp](https://github.com/dotnet/roslyn) | MIT | C# parsing for the source-scanning guards |
| [NUnit](https://github.com/nunit/nunit) | MIT | The test framework the guard fixture is written for |

## Trax.Api.GraphQL.Client

| Package | Licence | Used for |
|---|---|---|
| [GraphQL.Client](https://github.com/graphql-dotnet/graphql-client) | MIT | GraphQL over HTTP |
| [GraphQL.Client.Serializer.SystemTextJson](https://github.com/graphql-dotnet/graphql-client) | MIT | System.Text.Json serialization for GraphQL.Client |
| [GraphQL](https://github.com/graphql-dotnet/graphql-dotnet) | MIT | Schema parsing for request validation |
| [Microsoft.Extensions.Http](https://github.com/dotnet/runtime) | MIT | HttpClient factory |
| [Microsoft.Extensions.DependencyInjection.Abstractions](https://github.com/dotnet/runtime) | MIT | Service registration |
| [Microsoft.Extensions.Logging.Abstractions](https://github.com/dotnet/runtime) | MIT | Logging abstractions |

## Trax.Api.GraphQL.Client.Trax

| Package | Licence | Used for |
|---|---|---|
| [HotChocolate](https://github.com/ChilliCream/graphql-platform) | MIT | Building the server's schema in-process |
| [Microsoft.Extensions.Hosting.Abstractions](https://github.com/dotnet/runtime) | MIT | Startup validation as a hosted service |
| [Microsoft.Extensions.Logging.Abstractions](https://github.com/dotnet/runtime) | MIT | Logging abstractions |

## Trax.Dashboard

| Package | Licence | Used for |
|---|---|---|
| [Radzen.Blazor](https://github.com/radzenhq/radzen-blazor) | MIT | Blazor UI component library for the dashboard |

## Trax.Cli

| Package | Licence | Used for |
|---|---|---|
| [System.CommandLine](https://github.com/dotnet/command-line-api) | MIT | Command-line parsing |
| [GraphQL-Parser](https://github.com/graphql-dotnet/parser) | MIT | GraphQL SDL parsing |
| [Microsoft.OpenApi.Readers](https://github.com/microsoft/OpenAPI.NET) | MIT | OpenAPI document parsing |
| [Scriban](https://github.com/scriban/scriban) | BSD 2-Clause | Template engine for code generation |
