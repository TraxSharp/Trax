---
title: IPersistedOperationValidator
description: "Reference for IPersistedOperationValidator and its HotChocolate and no-op implementations, which validate a document before it is stored."
type: reference
area: api
parent: Persisted Operations
grand_parent: SDK Reference
nav_order: 8
package: Trax.Api.GraphQL.PersistedOperations
namespace: Trax.Api.GraphQL.PersistedOperations.Storage.Validation
source:
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Storage/Validation/IPersistedOperationValidator.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Storage/Validation/HotChocolateSchemaValidator.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Storage/Validation/NoOpPersistedOperationValidator.cs
---

# IPersistedOperationValidator

Validates a candidate GraphQL document before [IPersistedOperationStore.UpsertAsync](/docs/sdk-reference/persisted-operations/i-persisted-operation-store) writes a row.

## Signature

```csharp fragment
public interface IPersistedOperationValidator
{
    Task ValidateAsync(string document, CancellationToken ct);
}

public sealed class HotChocolateSchemaValidator : IPersistedOperationValidator
{
    public HotChocolateSchemaValidator(IServiceProvider services, string schemaName = "trax");
}

public sealed class NoOpPersistedOperationValidator : IPersistedOperationValidator
{
    public NoOpPersistedOperationValidator();
}
```

Implementations throw one of the [PersistedOperationException](/docs/sdk-reference/persisted-operations/persisted-operation-exceptions) subclasses on failure. The storage layer lets these propagate; no row is written and no broadcast fires when validation throws.

## Members

| Implementation | Registered by | Behavior |
|---|---|---|
| `HotChocolateSchemaValidator` | `UsePersistedOperations(...)` on `TraxGraphQLBuilder` | Parses with `Utf8GraphQLParser`, then runs HotChocolate's `DocumentValidator` against the live schema. Throws `PersistedOperationParseException` for syntax errors and `PersistedOperationValidationException` for schema mismatches (unknown field, wrong variable type, missing required variable, etc.). |
| `NoOpPersistedOperationValidator` | `AddPersistedOperationStore(...)` (standalone admin path) | Accepts any input. The shape-diff guardrail still runs in the store. |

## Behavior

`HotChocolateSchemaValidator` validates against the schema named `schemaName`, Trax's by default. It resolves HotChocolate's `IRequestExecutorProvider` lazily on first call so the validator can be constructed before GraphQL composition is final. The resolved executor and the schema's own `DocumentValidator` are cached for the lifetime of the validator.

## Example

`UpsertAsync` calls the validator; it can also be resolved directly:

```csharp compile
var validator = serviceProvider.GetRequiredService<IPersistedOperationValidator>();

try
{
    await validator.ValidateAsync(candidateDocument, ct);
}
catch (PersistedOperationValidationException ex)
{
    foreach (var failure in ex.Failures)
        Console.Error.WriteLine($"{failure.Message} (line {failure.Locations.FirstOrDefault().Line})");
}
```

## See also

- [IPersistedOperationStore](/docs/sdk-reference/persisted-operations/i-persisted-operation-store)
- [PersistedOperationException](/docs/sdk-reference/persisted-operations/persisted-operation-exceptions)
