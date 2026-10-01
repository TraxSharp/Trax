---
layout: default
title: Getting Started
nav_order: 2
---

# Getting Started

Trax requires `net10.0`:

```xml
<TargetFramework>net10.0</TargetFramework>
```

Every project that references a Trax package must target `net10.0`; there is no `net8.0` or
`net9.0` build.

Trax's packages depend on EF Core, Npgsql and `Microsoft.Extensions.*`, so NuGet resolves those
for you. You only need to act if your project also pins one of them directly: the pin has to be
at least the version the Trax package was built against, or the restore fails with `NU1605` (`NU1109`
under Central Package Management) naming the package. The floors as of `Trax.Effect` 1.57 are:

| Package | Minimum |
|---|---|
| `Microsoft.EntityFrameworkCore` (and `.Relational`, `.InMemory`, `.Sqlite`) | 10.0.12 |
| `Npgsql` | 10.0.3 |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.3 |
| `EFCore.NamingConventions` | 10.0.1 |
| `Microsoft.Extensions.*` | 10.0.12 |

A release can raise them; the dependency list on the package's nuget.org page is authoritative.

Pick the track that matches what you need:

---

## Track 1: Core Only

Type-safe pipelines with no infrastructure. No database, no DI container, no ASP.NET required.

```bash
dotnet add package Trax.Core
```

### Define Junctions

```csharp
public class ValidateEmailJunction : Junction<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        if (!IsValidEmail(input.Email))
            throw new ValidationException("Invalid email format");
        return Unit.Default;
    }

    private static bool IsValidEmail(string email)
        => new EmailAddressAttribute().IsValid(email);
}

public class FormatNameJunction : Junction<CreateUserRequest, FullName>
{
    public override Task<FullName> Run(CreateUserRequest input)
        => Task.FromResult(new FullName($"{input.FirstName} {input.LastName}"));
}
```

### Define a Train

```csharp
public class CreateUserTrain : Train<CreateUserRequest, FullName>
{
    protected override Task<Either<Exception, FullName>> Junctions() =>
        Chain<ValidateEmailJunction>()
            .Chain<FormatNameJunction>().Resolve();
}
```

### Run It

```csharp
var train = new CreateUserTrain();
var result = await train.RunEither(new CreateUserRequest
{
    Email = "test@example.com",
    FirstName = "Test",
    LastName = "User"
});

result.Match(
    Left: ex => Console.WriteLine($"Failed: {ex.Message}"),
    Right: name => Console.WriteLine($"Created: {name}")
);
```

**Next:** [Core docs](/docs/core) for Memory, chain methods, and IDE extensions.

---

## Track 2: Core + Effect

Add execution logging, DI, and persistent metadata. Every train run becomes a queryable record.

```bash
dotnet add package Trax.Core
dotnet add package Trax.Effect
dotnet add package Trax.Effect.Data.Postgres  # or Trax.Effect.Data.Sqlite / Trax.Effect.Data.InMemory
```

### Program.cs Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(builder.Configuration.GetConnectionString("TraxDatabase")!)
        .SaveTrainParameters()
    )
);

builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IEmailService, EmailService>();

var app = builder.Build();
app.Run();
```

### Use ServiceTrain

Switch from `Train` to `ServiceTrain` to get metadata tracking:

```csharp
public interface ICreateUserTrain : IServiceTrain<CreateUserRequest, User>;

public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override Task<Either<Exception, User>> Junctions() =>
        Chain<ValidateEmailJunction>()
            .Chain<CreateUserInDatabaseJunction>()
            .Chain<SendWelcomeEmailJunction>()
            .Resolve();
}
```

The `Junctions()` code is identical to Core. `ServiceTrain` adds the execution logging and DI around it.

**Next:** [Effect docs](/docs/effect) for metadata, effect providers, and the ServiceTrain lifecycle.

---

## Track 3: Full Stack

Add the mediator, scheduler, and dashboard for a complete platform.

```bash
dotnet new install Trax.Samples.Templates
dotnet new trax-scheduler -n MyApp
```

This scaffolds a project with:
- The in-memory data provider, so it runs with no database (swap in `UsePostgres` when you need one)
- `TrainBus` for decoupled dispatch
- A scheduler running a sample HelloWorld train every 20 seconds
- The dashboard at `/trax`, in Development only

`trax-hub` adds the GraphQL API to the same process. See [Project Templates](/docs/reference/templates).

Or configure manually:

```csharp
builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects
        .UsePostgres(connectionString)
        .SaveTrainParameters()
        .AddJunctionLogger(serializeJunctionData: true)
        .AddJunctionProgress()
    )
    .AddMediator(typeof(Program).Assembly)
    .AddScheduler()   // the dashboard works through the Scheduler
);

// Who may use the dashboard. AllowAnonymousDashboard() is for local development only.
builder.Services.AddTraxDashboard(o =>
{
    if (builder.Environment.IsDevelopment())
        o.AllowAnonymousDashboard();
    else
        o.RequirePolicy("TraxAdmin");
});

var app = builder.Build();

app.UseTraxDashboard();
app.Run();
```

**Next:**
- [Mediator](/docs/mediator): decoupled dispatch with TrainBus
- [Scheduling](/docs/scheduler): cron jobs, retries, dead letters
- [Dashboard](/docs/dashboard): monitoring UI
- [API](/docs/api): GraphQL interface
- [Project Template](/docs/reference/templates): full template reference
- [Samples & Deployment](/docs/samples): the trains library pattern and deployment topologies

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Run / RunEither](/docs/sdk-reference/train-methods/run) | [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UsePostgres](/docs/sdk-reference/configuration/add-postgres-effect) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddJunctionLogger](/docs/sdk-reference/configuration/add-junction-logger) | [AddJunctionProgress](/docs/sdk-reference/configuration/add-junction-progress) | [AddMediator](/docs/sdk-reference/mediator-api/add-service-train-bus) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard) | [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard)
