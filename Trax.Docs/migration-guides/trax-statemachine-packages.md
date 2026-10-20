---
title: Move to the Trax.StateMachine packages
description: "Upgrade steps for the release that renamed the state-machine packages from Trax.Effect.StateMachine to Trax.StateMachine: swap the ids and namespaces."
type: how-to
area: upgrading
parent: Upgrading
nav_order: 0
---

# Move to the Trax.StateMachine packages

Applies from: the first Trax release after 1.67.0.

The state machines left Trax.Effect for a folder of their own, and their packages and namespaces
were renamed to match. Nothing else changed: the types, their members, the tables and the wire are
the same, and `@traxsharp/state-machine` on npm keeps its name.

| Before | After |
|---|---|
| `Trax.Effect.StateMachine` | `Trax.StateMachine` |
| `Trax.Effect.StateMachine.Persistence` | `Trax.StateMachine.Persistence` |
| `Trax.Effect.StateMachine.Testing` | `Trax.StateMachine.Testing` |

The old packages are deprecated on nuget.org and get no further releases.

## Swap the packages

In each project that references one of the old packages, replace it with the new id at the same
version as your other Trax packages:

```xml
<PackageReference Include="Trax.StateMachine.Persistence" />
```

With Central Package Management, rename the `PackageVersion` in `Directory.Packages.props` the
same way.

## Swap the namespaces

Each namespace loses its `Effect.` segment, the same way the package ids do:

```csharp fragment
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;
```

A search and replace of `Trax.Effect.StateMachine` with `Trax.StateMachine` over your sources and
project files does both steps. Nothing else references the old names: a stored snapshot, an IR or a
generated twin carries no namespace, so neither the database nor a regenerated client changes.
