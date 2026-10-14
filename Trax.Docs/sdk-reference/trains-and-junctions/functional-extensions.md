---
title: FunctionalExtensions
description: "Reference for AssertLoaded and AssertEachLoaded, which throw when a value or a navigation on each element of a collection has not been loaded."
type: reference
area: trains
package: Trax.Core
namespace: Trax.Core.Extensions
source:
  - Trax.Core/src/Trax.Core/Extensions/FunctionalExtensions.cs
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 10
---

# FunctionalExtensions

Extension methods that throw when a value that should have been loaded is `null`, naming the expression that was checked. They stand in for the null-forgiving operator (`!`) where a `null` is a bug worth reporting, such as an EF navigation that was not included in a query.

## Signatures

```csharp fragment
namespace Trax.Core.Extensions;

public static class FunctionalExtensions
{
    public static void AssertLoaded<T>(
        [NotNull] this T? value,
        [CallerArgumentExpression("value")] string? valueExpr = null
    );

    public static void AssertEachLoaded<T, U>(
        [NotNull] this IEnumerable<T> values,
        Func<T, U> selector,
        [CallerArgumentExpression("values")] string? valuesExpr = null,
        [CallerArgumentExpression("selector")] string? selectorExpr = null
    );
}
```

## Parameters

| Parameter | Description |
|-----------|-------------|
| `value` | The value to check |
| `values` | The collection whose elements are checked |
| `selector` | What to check on each element, such as a navigation property |
| `valueExpr`, `valuesExpr`, `selectorExpr` | Filled by the compiler with the caller's expressions; not passed by hand |

## Behavior

| Method | Throws `InvalidOperationException` when | Message |
|--------|------------------------------------------|---------|
| `AssertLoaded` | `value` is `null` | `<valueExpr> has not been loaded` |
| `AssertEachLoaded` | `selector` returns `null` for any element | `<valuesExpr>(<selectorExpr>) has not been loaded` |

After `AssertLoaded` returns, the compiler treats `value` as not null (`[NotNull]`).

## Example

```csharp compile
using Trax.Core.Extensions;

public record Customer(string Name);

public record Invoice(long Id, Customer? Customer);

public static class InvoiceNames
{
    public static IEnumerable<string> CustomerNames(IReadOnlyList<Invoice> invoices)
    {
        invoices.AssertEachLoaded(i => i.Customer);
        return invoices.Select(i => i.Customer!.Name);
    }

    public static string CustomerName(Invoice invoice)
    {
        invoice.Customer.AssertLoaded();
        return invoice.Customer.Name;
    }
}
```

## See also

- [Either and Unit](/docs/sdk-reference/trains-and-junctions/either)
