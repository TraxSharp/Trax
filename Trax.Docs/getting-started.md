---
layout: default
title: Getting Started
nav_order: 2
---

# Getting Started

## Prerequisites

Trax.Core 5.x targets `net10.0` exclusively. Make sure your project's target framework is set accordingly:

```xml
<TargetFramework>net10.0</TargetFramework>
```

If you're upgrading from 4.x, see [Migration (4.x → 5.x)](migration.md).

For a complete starter project with scheduling, persistence, and dashboards already configured, see the [Project Template](templates.md).

## Installation

### Install NuGet Packages

For a typical setup with database persistence and train discovery:

```xml
<PackageReference Include="Trax.Core" Version="5.*" />
<PackageReference Include="Trax.Effect" Version="5.*" />
```

Or via the .NET CLI:

```bash
dotnet add package Trax.Core
dotnet add package Trax.Effect
```

## Basic Configuration

### Program.cs Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Trax.Core services
builder.Services.AddTrax.CoreEffects(o => o.AddServiceTrainBus(typeof(Program).Assembly));

// Add your application services
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IEmailService, EmailService>();

var app = builder.Build();

app.Run();
```

*API Reference: [AddTrax.CoreEffects]({{ site.baseurl }}{% link api-reference/configuration.md %}), [AddServiceTrainBus]({{ site.baseurl }}{% link api-reference/configuration/add-effect-train-bus.md %})*

## Creating Your First Train

### 1. Define Input and Output Models

```csharp
// Input model - unique per train
public record CreateUserRequest
{
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string? PhoneNumber { get; init; }
}

// Output model
public record User
{
    public int Id { get; init; }
    public string Email { get; init; }
    public string FullName { get; init; }
    public DateTime CreatedAt { get; init; }
}
```

### 2. Implement the Train

```csharp
public interface ICreateUserTrain : IServiceTrain<CreateUserRequest, User> { }

public class CreateUserTrain : ServiceTrain<CreateUserRequest, User>, ICreateUserTrain
{
    protected override async Task<Either<Exception, User>> RunInternal(CreateUserRequest input)
        => Activate(input)
            .Chain<ValidateEmailStep>()
            .Chain<CreateUserInDatabaseStep>()
            .Chain<SendWelcomeEmailStep>()
            .Resolve();
}
```

*API Reference: [Activate]({{ site.baseurl }}{% link api-reference/train-methods/activate.md %}), [Chain]({{ site.baseurl }}{% link api-reference/train-methods/chain.md %}), [Resolve]({{ site.baseurl }}{% link api-reference/train-methods/resolve.md %})*

### 3. Implement the Steps

```csharp
public class ValidateEmailStep(IUserRepository UserRepository) : Step<CreateUserRequest, Unit>
{
    public override async Task<Unit> Run(CreateUserRequest input)
    {
        // Check if email already exists
        var existingUser = await UserRepository.GetByEmailAsync(input.Email);
        if (existingUser != null)
            throw new ValidationException($"User with email {input.Email} already exists");
        
        // Validate email format
        if (!IsValidEmail(input.Email))
            throw new ValidationException("Invalid email format");
            
        return Unit.Default;
    }
    
    private static bool IsValidEmail(string email)
        => new EmailAddressAttribute().IsValid(email);
}

public class CreateUserInDatabaseStep(IUserRepository UserRepository) : Step<CreateUserRequest, User>
{
    public override async Task<User> Run(CreateUserRequest input)
    {
        var user = new User
        {
            Email = input.Email,
            FullName = $"{input.FirstName} {input.LastName}",
            CreatedAt = DateTime.UtcNow
        };
        
        return await UserRepository.CreateAsync(user);
    }
}

public class SendWelcomeEmailStep(IEmailService EmailService) : Step<User, Unit>
{
    public override async Task<Unit> Run(User input)
    {
        await EmailService.SendWelcomeEmailAsync(input.Email, input.FullName);
        return Unit.Default;
    }
}
```

### 4. Use the Train

```csharp
[ApiController]
[Route("api/[controller]")]
public class UsersController(ITrainBus trainBus) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<User>> CreateUser(CreateUserRequest request)
    {
        try
        {
            var user = await trainBus.RunAsync<User>(request);
            return Created($"/api/users/{user.Id}", user);
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, "An error occurred while creating the user");
        }
    }
}
```

*API Reference: [TrainBus.RunAsync]({{ site.baseurl }}{% link api-reference/mediator-api/train-bus.md %})*

## Next Steps

- [Core Concepts](concepts.md) - Understand the ideas behind Trax.Core
- [Usage Guide](usage-guide.md) - Patterns and examples for building trains
- [Mediator](usage-guide/mediator.md) - Dispatch trains with the TrainBus
- [Scheduling](scheduler.md) - Background job orchestration with retries and dead-lettering
- [Architecture](architecture.md) - How the system is built internally
- [Dashboard](dashboard.md) - Add a web UI for inspecting trains (optional)
