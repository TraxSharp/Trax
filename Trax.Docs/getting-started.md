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

If you're migrating from ChainSharp, see [Migration Guide]({{ site.baseurl }}{% link reference/migration.md %}).

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
    protected override async Task<Either<Exception, FullName>> RunInternal(CreateUserRequest input)
        => Activate(input)
            .Chain<ValidateEmailJunction>()
            .Chain<FormatNameJunction>()
            .Resolve();
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

**Next:** [Core docs]({{ site.baseurl }}{% link core.md %}) for Memory, the Analyzer, chain methods, and IDE extensions.

---

## Track 2: Core + Effect

Add execution logging, DI, and persistent metadata. Every train run becomes a queryable record.

```bash
dotnet add package Trax.Core
dotnet add package Trax.Effect
dotnet add package Trax.Effect.Data.Postgres  # or Trax.Effect.Data.InMemory
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
    protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
        => Activate(input)
            .Chain<ValidateEmailJunction>()
            .Chain<CreateUserInDatabaseJunction>()
            .Chain<SendWelcomeEmailJunction>()
            .Resolve();
}
```

The `RunInternal` code is identical to Core — `ServiceTrain` adds the execution logging and DI around it.

**Next:** [Effect docs]({{ site.baseurl }}{% link effect.md %}) for metadata, effect providers, and the ServiceTrain lifecycle.

---

## Track 3: Full Stack

Add the mediator, scheduler, and dashboard for a complete platform.

```bash
dotnet new install Trax.Samples.Templates
dotnet new trax-scheduler -n MyApp
```

This scaffolds a project with:
- PostgreSQL persistence
- `TrainBus` for decoupled dispatch
- Scheduler with cron-based manifests
- Dashboard at `/trax`
- A sample HelloWorld train

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
);

builder.Services.AddTraxDashboard();

var app = builder.Build();

app.UseTraxDashboard();
app.Run();
```

**Next:**
- [Mediator]({{ site.baseurl }}{% link mediator.md %}) — decoupled dispatch with TrainBus
- [Scheduling]({{ site.baseurl }}{% link scheduler.md %}) — cron jobs, retries, dead letters
- [Dashboard]({{ site.baseurl }}{% link dashboard.md %}) — monitoring UI
- [API]({{ site.baseurl }}{% link api.md %}) — GraphQL interface
- [Project Template]({{ site.baseurl }}{% link reference/templates.md %}) — full template reference
- [Samples & Deployment]({{ site.baseurl }}{% link samples.md %}) — the trains library pattern and deployment topologies
