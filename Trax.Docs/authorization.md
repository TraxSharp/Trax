---
layout: default
title: Authorization
nav_order: 9
section: Guides
---

> **Security notice.** NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for securing systems that use it. See [API Security](/docs/api-security).

# Authorization

Trax supports two levels of authorization for the API layer:

- **Endpoint-level**: gate all Trax endpoints behind a single policy using the `configure` callback on `UseTraxGraphQL`. This is standard ASP.NET Core endpoint authorization.
- **Per-train**: restrict individual trains using the `[TraxAuthorize]` attribute on the train class. When a request comes in to run or queue a train, Trax checks the attribute against the current HTTP user before executing anything.

Endpoint-level auth answers "can this user access the Trax API at all?" Per-train auth answers "can this user execute *this particular* train?"

## Per-Train Authorization

Decorate any train class with `[TraxAuthorize]` to declare authorization requirements:

```csharp
using Trax.Effect.Attributes;

// Requires an authenticated user. No specific policy or role.
[TraxAuthorize]
public class WhoAmITrain : ServiceTrain<Unit, UserInfo>, IWhoAmITrain
{
    protected override Task<Either<Exception, UserInfo>> Junctions() =>
        Chain<ReadUserJunction>().Resolve();
}

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

// No attribute, no per-train auth check
public class PingTrain : ServiceTrain<PingInput, PongOutput>, IPingTrain
{
    protected override PongOutput Junctions() => Chain<PingJunction>();
}
```

The attribute supports two properties:

| Property | Type | Description |
|----------|------|-------------|
| `Policy` | `string?` | Name of an ASP.NET Core authorization policy to evaluate |
| `Roles` | `string?` | Comma-separated list of roles. The user must have at least one. Role comparison is case-insensitive. |

The attribute works on classes, interfaces, and base classes. Trax unions the attributes it finds across the implementation type's interface chain and base chain, so `[TraxAuthorize("Admin")]` on an `IMyTrain` interface is honored even when the implementing class carries no attribute. Decorator-wrapped trains inherit their authorization requirements through the same mechanism.

Role comparison is case-insensitive. `[TraxAuthorize(Roles = "admin")]` matches a principal carrying `ClaimTypes.Role = "Admin"` and vice-versa — both sides are normalized to upper-invariant.

## How Policies and Roles Combine

Multiple attributes and mixed policy/role specifications combine as follows:

| Attribute form | What the user must satisfy |
|----------------|---------------------------|
| `[TraxAuthorize]` (bare) | Be authenticated. No additional policy or role requirement. |
| `[TraxAuthorize("P")]` | Policy `P` must pass. |
| `[TraxAuthorize(Roles = "A,B")]` | Hold role `A` OR role `B`. |
| `[TraxAuthorize("P", Roles = "A")]` | Policy `P` must pass AND hold role `A`. |
| `[TraxAuthorize("P1")] [TraxAuthorize("P2")]` | Both `P1` and `P2` must pass. Policies AND across attributes. |
| `[TraxAuthorize(Roles = "A")] [TraxAuthorize(Roles = "B")]` | Hold role `A` OR role `B`. Roles union OR across attributes. |
| `[TraxAuthorize("P")] [TraxAuthorize(Roles = "A")]` | Policy `P` must pass AND hold role `A`. |

In short: every policy has to pass, and at least one role from the unioned list has to match. A bare attribute requires nothing beyond authentication.

```csharp
[TraxAuthorize("MustBeInternal")]
[TraxAuthorize(Roles = "Admin,Manager")]
public class SensitiveTrain : ServiceTrain<SensitiveInput, Unit>, ISensitiveTrain { ... }
```

The caller must pass the `MustBeInternal` policy and hold either `Admin` or `Manager`.

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

1. `ITrainDiscoveryService` reads `[TraxAuthorize]` attributes across the implementation, its base chain, and every implemented interface. Roles are normalized to upper-invariant; policies are deduplicated. The requirements are stored on each `TrainRegistration`.
2. At host start, `AuthorizationRegistrationValidator` runs as a hosted service. It throws if any train carries `[TraxAuthorize]` but no `ITrainAuthorizationService` is registered (this can be opted out of per below), and it throws on malformed attribute shapes (empty policy strings, whitespace-only roles) so typos are caught before traffic arrives.
3. When `ITrainExecutionService.QueueAsync()` or `RunAsync()` runs, it invokes the registered `ITrainAuthorizationService`.
4. The default implementation (`TrainAuthorizationService` from `Trax.Api`) is fail-closed. It grabs the current user from `IHttpContextAccessor` and evaluates each requirement:
   - **Policy**: calls `IAuthorizationService.AuthorizeAsync(user, policyName)`.
   - **Roles**: compares the upper-invariant `ClaimTypes.Role` claims against the normalized required set.
5. If any check fails, `TrainAuthorizationException` is thrown. Its public `Message` is always the generic string `"Not authorized."`; the train name, failing policy, and required roles live only on the exception's `TrainName` and `Reason` properties for server-side logging.
6. GraphQL surfaces the error with code `TRAX_AUTHORIZATION` and the same generic message. The train name, policy name, and role names never cross the wire.

### Fail-Closed Behavior

When an `HttpContext` is absent and no trusted execution scope is active, the service denies. This protects against accidental invocation from background services, tests, or custom middleware that bypasses the normal request pipeline. Scheduler and remote-worker paths explicitly mark themselves as trusted via `ITrustedExecutionScope.BeginTrusted(...)` so pre-authorized queued work still runs.

### Opting Out for Scheduler-Only Hosts

A process that registers `[TraxAuthorize]` trains for discovery but never serves API submissions (e.g. a standalone scheduler worker) can opt out of the fail-closed startup check:

```csharp
services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connStr))
    .AddMediator(mediator => mediator
        .ScanAssemblies(typeof(Program).Assembly)
        .AllowMissingAuthorizationService()));
```

Only use this from processes that genuinely never accept API submissions. The flag disables the fail-closed guard; every `[TraxAuthorize]` gated train will run without authorization checks in that process.

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

## The Scheduler and Remote Workers Are Trusted

Authorization is enforced once, at API submission time. When a train is queued or run through GraphQL, the request carries an `HttpContext` and the authorization check evaluates against the caller's identity. Work that makes it past that check is written to the queue as already-authorized.

Later execution paths are trusted:

- The scheduler dequeues from `work_queue` and calls `ITrainBus.RunAsync()` directly. It never reaches `ITrainAuthorizationService`.
- Remote workers pull queued work over HTTP and execute it via `ITrainExecutionService.RunAsync()`. Because there is no `HttpContext` on the worker side, the authorization check treats the caller as trusted infrastructure and skips.

This means you can safely decorate a train with `[TraxAuthorize("Admin")]` and still schedule it via `AddScheduler()`, run it from a remote worker, or both. The authorization gate is the API boundary.

## Custom Authorization Logic

If policies and roles aren't enough, you can replace the default `ITrainAuthorizationService` with your own implementation:

```csharp
public class CustomTrainAuthorizationService : ITrainAuthorizationService
{
    public async Task AuthorizeAsync(
        TrainRegistration registration,
        CancellationToken ct = default)
    {
        // Your custom logic here: check a database, call an external service, etc.
        // Throw to deny, return normally to allow.
    }
}

// Register before AddTraxGraphQL (which calls AddTraxApi internally)
builder.Services.AddScoped<ITrainAuthorizationService, CustomTrainAuthorizationService>();
```

The interface is defined in `Trax.Mediator`, so your implementation doesn't need to reference `Trax.Api`.

## SDK Reference

> [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [UseTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [ITrainDiscoveryService](/docs/sdk-reference/mediator-api/train-discovery) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/train-execution) | [TraxQuery / TraxMutation](/docs/sdk-reference/graphql-api/trax-graphql-attribute)
