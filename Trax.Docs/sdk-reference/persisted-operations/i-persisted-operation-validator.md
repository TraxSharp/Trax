---
layout: default
title: IPersistedOperationValidator
parent: Persisted Operations
grand_parent: SDK Reference
---

# IPersistedOperationValidator

Validates a candidate GraphQL document before [IPersistedOperationStore.UpsertAsync](/docs/sdk-reference/persisted-operations/i-persisted-operation-store) writes a row. Runs at upload time so operators see schema mismatches immediately rather than at request time when a client tries to run the operation.

## Interface

```csharp
public interface IPersistedOperationValidator
{
    Task ValidateAsync(string document, CancellationToken ct);
}
```

Implementations throw one of the [PersistedOperationException](/docs/sdk-reference/persisted-operations/persisted-operation-exceptions) subclasses on failure. The storage layer lets these propagate; no row is written and no broadcast fires when validation throws.

## Implementations

| Implementation | Registered by | Behavior |
|---|---|---|
| `HotChocolateSchemaValidator` | `UsePersistedOperations(...)` on `TraxGraphQLBuilder` | Parses with `Utf8GraphQLParser`, then runs HotChocolate's `IDocumentValidator` against the live schema. Throws `PersistedOperationParseException` for syntax errors and `PersistedOperationValidationException` for schema mismatches (unknown field, wrong variable type, missing required variable, etc.). |
| `NoOpPersistedOperationValidator` | `AddPersistedOperationStore(...)` (standalone admin path) | Accepts any input. Use this in hosts that do not have a HotChocolate schema in process (CI manifest uploaders, one-off scripts). The shape-diff guardrail still runs at the storage layer. |

The HotChocolate-backed implementation resolves `IRequestExecutorResolver` lazily on first call so the validator can be constructed before GraphQL composition is final. The resolved executor and the schema-specific `IDocumentValidator` are cached for the lifetime of the validator.

## Example

The validator usually fires implicitly inside `UpsertAsync`, but it can be resolved directly when admin tooling wants to validate before posting a payload:

```csharp
var validator = serviceProvider.GetRequiredService<IPersistedOperationValidator>();

try
{
    await validator.ValidateAsync(candidateDocument, ct);
}
catch (PersistedOperationValidationException ex)
{
    foreach (var failure in ex.Failures)
        Console.Error.WriteLine($"{failure.Message} (line {failure.Locations.FirstOrDefault().Line})");
    return;
}
```
