---
layout: default
title: Authorization
nav_order: 9
section: Guides
---

# Authorization

Trax supports two levels of authorization for the API layer:

- **Endpoint-level** — gate all Trax endpoints behind a single policy using the `configure` callback on `UseTraxGraphQL`. This is standard ASP.NET Core endpoint authorization.
- **Per-train** — restrict individual trains using the `[TraxAuthorize]` attribute on the train class. When a request comes in to run or queue a train, Trax checks the attribute against the current HTTP user before executing anything.

Endpoint-level auth answers "can this user access the Trax API at all?" Per-train auth answers "can this user execute *this particular* train?"

## Per-Train Authorization

Decorate any train class with `[TraxAuthorize]` to declare authorization requirements:

```csharp
using Trax.Effect.Attributes;

// Requires the "Admin" authorization policy
[TraxAuthorize("Admin")]
public class DeleteUserTrain : ServiceTrain<DeleteUserInput, Unit>, IDeleteUserTrain
{
    protected override Unit Junctions() => Chain<DeleteUserJunction>();
}

// Requires the user to have at least one of the listed roles
[TraxAuthorize(Roles = "Manager,Admin")]
public class GenerateReportTrain : ServiceTrain<ReportInput, ReportOutput>, IGenerateReportTrain
{
    protected override ReportOutput Junctions() => Chain<GenerateReportJunction>();
}

// No attribute — no per-train auth check
public class PingTrain : ServiceTrain<PingInput, PongOutput>, IPingTrain
{
    protected override PongOutput Junctions() => Chain<PingJunction>();
}
```

The attribute supports two properties:

| Property | Type | Description |
|----------|------|-------------|
| `Policy` | `string?` | Name of an ASP.NET Core authorization policy to evaluate |
| `Roles` | `string?` | Comma-separated list of roles — the user must have at least one |

You can apply multiple attributes. All must pass:

```csharp
[TraxAuthorize("MustBeInternal")]
[TraxAuthorize(Roles = "Admin")]
public class SensitiveTrain : ServiceTrain<SensitiveInput, Unit>, ISensitiveTrain { ... }
```

## Registering Policies

Trax uses standard ASP.NET Core authorization policies. Register them in `Program.cs` the same way you would for any controller or endpoint:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("MustBeInternal", policy =>
        policy.RequireClaim("network", "internal"));
});
```

Trax evaluates these policies at runtime using ASP.NET Core's `IAuthorizationService`. If a train requires a policy that isn't registered, the authorization check fails.

## How It Works

1. `ITrainDiscoveryService` reads `[TraxAuthorize]` attributes during discovery and stores the policy/role requirements on each `TrainRegistration`.
2. When `ITrainExecutionService.QueueAsync()` or `RunAsync()` is called, it checks whether an `ITrainAuthorizationService` is registered in DI.
3. The default implementation (`TrainAuthorizationService` from `Trax.Api`) grabs the current user from `IHttpContextAccessor` and evaluates each requirement:
   - **Policy**: calls `IAuthorizationService.AuthorizeAsync(user, policyName)`
   - **Roles**: calls `user.IsInRole(roleName)` for each role in the list
4. If any check fails, a `TrainAuthorizationException` is thrown before the train executes.
5. GraphQL surfaces it as a standard error in the `errors` array.

## Train Discovery Shows Auth Requirements

The GraphQL `trains` query includes authorization metadata in the response. Consumers can use this to build UIs that show which trains are available and what access they require.

```json
{
  "serviceTypeName": "IDeleteUserTrain",
  "implementationTypeName": "DeleteUserTrain",
  "inputTypeName": "DeleteUserInput",
  "outputTypeName": "Unit",
  "lifetime": "Transient",
  "inputSchema": [ ... ],
  "requiredPolicies": ["Admin"],
  "requiredRoles": []
}
```

Trains without `[TraxAuthorize]` return empty arrays for both fields.

## Combining with Endpoint-Level Auth

Per-train auth and endpoint-level auth are complementary. A typical setup might look like:

```csharp
// All Trax endpoints require authentication
app.UseTraxGraphQL(configure: endpoint => endpoint
    .RequireAuthorization());

// Individual trains require specific policies
[TraxAuthorize("Admin")]
[TraxMutation]
public class AdminOnlyTrain : ServiceTrain<AdminInput, Unit>, IAdminOnlyTrain { ... }
```

The endpoint-level check runs first (before the request reaches the handler). The per-train check runs inside the handler, after the train is resolved by name.

## The Scheduler Bypasses Auth

The scheduler dequeues work items from the `work_queue` table and calls `ITrainBus.RunAsync()` directly — it never goes through `ITrainExecutionService`. Authorization is about whether a *user* can request a train, not whether the scheduler can execute one. The scheduler is trusted infrastructure.

This means you can safely decorate a train with `[TraxAuthorize("Admin")]` and still schedule it via `AddScheduler()`. The schedule runs on the scheduler machine without any HTTP context, and the authorization check doesn't apply.

## Custom Authorization Logic

If policies and roles aren't enough, you can replace the default `ITrainAuthorizationService` with your own implementation:

```csharp
public class CustomTrainAuthorizationService : ITrainAuthorizationService
{
    public async Task AuthorizeAsync(
        TrainRegistration registration,
        CancellationToken ct = default)
    {
        // Your custom logic here — check a database, call an external service, etc.
        // Throw to deny, return normally to allow.
    }
}

// Register before AddTraxGraphQL (which calls AddTraxApi internally)
builder.Services.AddScoped<ITrainAuthorizationService, CustomTrainAuthorizationService>();
```

The interface is defined in `Trax.Mediator`, so your implementation doesn't need to reference `Trax.Api`.

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [UseTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [ITrainDiscoveryService](/docs/sdk-reference/mediator-api/train-discovery) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/train-execution) | [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute)
