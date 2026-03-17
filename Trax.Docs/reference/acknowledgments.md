---
layout: default
title: Acknowledgments
parent: Reference
nav_order: 5
---

# Acknowledgments

Trax is built on top of excellent open-source libraries. This page lists the third-party packages used across the Trax ecosystem, grouped by the Trax package that depends on them.

## Trax.Core

| Package | License | Description |
|---------|---------|-------------|
| [LanguageExt](https://github.com/louthy/language-ext) | MIT | Functional programming primitives (`Either`, `Unit`, `Option`) used throughout the train pipeline |
| [Microsoft.Extensions.DependencyInjection.Abstractions](https://github.com/dotnet/runtime) | MIT | DI abstractions for service registration |
| [Microsoft.Extensions.Logging.Abstractions](https://github.com/dotnet/runtime) | MIT | Logging abstractions |

## Trax.Effect

*No additional third-party packages beyond Trax.Core dependencies.*

## Trax.Effect.Data

| Package | License | Description |
|---------|---------|-------------|
| [Microsoft.EntityFrameworkCore](https://github.com/dotnet/efcore) | MIT | ORM for train metadata persistence |
| [Microsoft.EntityFrameworkCore.Relational](https://github.com/dotnet/efcore) | MIT | Relational database support for EF Core |
| [Npgsql](https://github.com/npgsql/npgsql) | PostgreSQL | .NET data provider for PostgreSQL |

## Trax.Effect.Data.Postgres

| Package | License | Description |
|---------|---------|-------------|
| [Npgsql.EntityFrameworkCore.PostgreSQL](https://github.com/npgsql/efcore.pg) | PostgreSQL | EF Core provider for PostgreSQL |
| [EFCore.NamingConventions](https://github.com/efcore/EFCore.NamingConventions) | Apache 2.0 | Snake_case column naming for PostgreSQL |
| [dbup-core](https://github.com/DbUp/DbUp) | MIT | Database migration framework |
| [dbup-postgresql](https://github.com/DbUp/dbup-postgresql) | MIT | PostgreSQL support for DbUp |

## Trax.Effect.Data.InMemory

| Package | License | Description |
|---------|---------|-------------|
| [Microsoft.EntityFrameworkCore.InMemory](https://github.com/dotnet/efcore) | MIT | In-memory database provider for development and testing |

## Trax.Scheduler

| Package | License | Description |
|---------|---------|-------------|
| [Cronos](https://github.com/HangfireIO/Cronos) | MIT | Cron expression parser for schedule definitions |

## Trax.Api.GraphQL

| Package | License | Description |
|---------|---------|-------------|
| [HotChocolate.AspNetCore](https://github.com/ChilliCream/graphql-platform) | MIT | GraphQL server for ASP.NET Core |
| [HotChocolate.Data](https://github.com/ChilliCream/graphql-platform) | MIT | Data integration for HotChocolate |
| [HotChocolate.Data.EntityFramework](https://github.com/ChilliCream/graphql-platform) | MIT | EF Core integration for HotChocolate |

## Trax.Dashboard

| Package | License | Description |
|---------|---------|-------------|
| [Radzen.Blazor](https://github.com/radzenhq/radzen-blazor) | MIT | Blazor UI component library for the dashboard |

## Trax.Effect.Broadcaster.RabbitMQ

| Package | License | Description |
|---------|---------|-------------|
| [RabbitMQ.Client](https://github.com/rabbitmq/rabbitmq-dotnet-client) | Apache 2.0 / MIT | RabbitMQ client for real-time subscription broadcasting |

## Trax.Scheduler.Sqs

| Package | License | Description |
|---------|---------|-------------|
| [AWSSDK.SQS](https://github.com/aws/aws-sdk-net) | Apache 2.0 | AWS SQS client for distributed job queuing |

## Trax.Runner.Lambda

| Package | License | Description |
|---------|---------|-------------|
| [Amazon.Lambda.Core](https://github.com/aws/aws-lambda-dotnet) | Apache 2.0 | AWS Lambda runtime support |
| [Amazon.Lambda.APIGatewayEvents](https://github.com/aws/aws-lambda-dotnet) | Apache 2.0 | API Gateway event types |
| [Amazon.Lambda.SQSEvents](https://github.com/aws/aws-lambda-dotnet) | Apache 2.0 | SQS event types for Lambda triggers |

## Trax.Cli

| Package | License | Description |
|---------|---------|-------------|
| [System.CommandLine](https://github.com/dotnet/command-line-api) | MIT | CLI argument parsing |
| [GraphQL-Parser](https://github.com/graphql-dotnet/parser) | MIT | GraphQL SDL schema parsing |
| [Microsoft.OpenApi.Readers](https://github.com/microsoft/OpenAPI.NET) | MIT | OpenAPI spec parsing |
| [Scriban](https://github.com/scriban/scriban) | BSD 2-Clause | Template engine for code generation |
