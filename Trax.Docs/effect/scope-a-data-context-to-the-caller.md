---
title: Scope a data context to the caller
description: "Register a domain data context whose query filters narrow every query to the calling user, with an unpooled scoped factory and a caller interface."
type: how-to
area: data
nav_order: 3
source:
  - Trax.Effect/src/Trax.Effect.Data/Services/DomainContext
  - Trax.Api/src/Trax.Api.Auth/TraxCaller.cs
---

# Scope a data context to the caller

A context whose rows belong to users (a member's loans, a customer's orders) narrows every query to the caller with an EF query filter, and the filter needs the caller. `AddDomainDataContext` registers a pooled factory, and a pooled context can only be built from its options, so it cannot take a scoped service such as the caller. This page registers such a context with an unpooled, scoped factory instead. The rest of the setup is as in [Add a domain data context](/docs/effect/effect-providers/domain-data-contexts).

## 1. Give the context the caller through an interface of its own

The data layer then does not depend on how callers are authenticated:

```csharp compile=lending
public interface ILendingCaller
{
    string? PrincipalId { get; }   // the qualified principal id, or null when anonymous
    bool IsLibrarian { get; }
}

public sealed class NoLendingCaller : ILendingCaller
{
    public static readonly NoLendingCaller Instance = new();
    public string? PrincipalId => null;
    public bool IsLibrarian => false;
}
```

## 2. Filter through a field of the context

```csharp compile=lending
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DomainContext;

public class LendingDbContext : DomainDataContext<LendingDbContext>, ILendingDbContext
{
    private readonly ILendingCaller _caller;

    [ActivatorUtilitiesConstructor]
    public LendingDbContext(DbContextOptions<LendingDbContext> options, ILendingCaller caller)
        : base(options) => _caller = caller;

    // For tools that build the context from its options alone: it sees no owner's rows.
    public LendingDbContext(DbContextOptions<LendingDbContext> options)
        : this(options, NoLendingCaller.Instance) { }

    public DbSet<Member> Members => Set<Member>();
    public DbSet<Loan> Loans => Set<Loan>();

    protected override string Schema => "lending";

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Member>().HasQueryFilter(m =>
            _caller.IsLibrarian || (_caller.PrincipalId != null && m.PrincipalId == _caller.PrincipalId));

        modelBuilder.Entity<Loan>().HasQueryFilter(l =>
            _caller.IsLibrarian
            || Set<Member>().Any(m => m.Id == l.MemberId
                && _caller.PrincipalId != null && m.PrincipalId == _caller.PrincipalId));
    }
}
```

- EF Core evaluates `_caller.PrincipalId` when each query runs, against the context running it, so one model serves every request.
- The options-only constructor is for the [architecture guards](/docs/reference/architecture-guards), which build every context in `DomainContexts` offline from its options. It passes a caller who sees nothing, so anything that builds the context that way fails closed.
- With two constructors, `[ActivatorUtilitiesConstructor]` tells the container and EF's factory which to use. Without it the factory throws "Multiple constructors accepting all given argument types have been found".

## 3. Bind the caller over TraxCaller

```csharp compile=lending
using Trax.Api.Auth;

public sealed class TraxLendingCaller(TraxCaller caller) : ILendingCaller
{
    public string? PrincipalId => caller.Principal?.Id;

    public bool IsLibrarian => caller.Principal?.Roles.Contains("librarian") == true;
}
```

[`TraxCaller`](/docs/sdk-reference/api-auth/trax-caller) never throws for an anonymous caller and reads the principal on each access, so a principal authenticated after the context was built (Trax authenticates multi-scheme GraphQL requests just before they execute) still applies. Every Trax auth scheme registers it; a host whose scheme is registered only in some environments also calls `AddTraxPrincipalAccessor()`.

## 4. Register a scoped factory, and the interface from it

```csharp compile=lending
using Microsoft.EntityFrameworkCore;

services.AddScoped<ILendingCaller, TraxLendingCaller>();
services.AddDbContextFactory<LendingDbContext>(o => o.UseNpgsql(connectionString), ServiceLifetime.Scoped);
services.AddScoped<ILendingDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<LendingDbContext>>().CreateDbContext());
```

`ServiceLifetime.Scoped` matters: a singleton factory resolves the context's constructor from the root container, where the scoped caller cannot be resolved. A scoped one builds each context with the caller of the request resolving it. `AddDbContextFactory` also registers `LendingDbContext` itself as scoped, which is what `AddTraxGraphQL().AddDbContext<LendingDbContext>()` resolves for the query models, so they read through the same filters as the trains.

## 5. Bypass the filter only where you mean to

A read that must see past the filter (whether anyone has a book out, a seed that runs at startup with no caller) calls `IgnoreQueryFilters()` on that one query. The [owner-scope census](/docs/reference/architecture-guards#the-owner-scope-census) reports every such call unless the file is listed in its `FilterBypassAllowlist` with a reason.

The [Bookworm sample](/docs/samples/bookworm) is a working owner-scoped context, with the census adopted and cross-user E2E tests.

## SDK Reference

> [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context) | [TraxCaller](/docs/sdk-reference/api-auth/trax-caller) | [Architecture guards](/docs/reference/architecture-guards)
