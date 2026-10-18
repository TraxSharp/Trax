---
title: "4.6 Two orders, one T-shirt"
description: "Chapter 4 of the Trax book, section 6: Two orders, one T-shirt."
type: tutorial
area: general
parent: "4. The shop's own data"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/the-shops-own-data/the-catalogue-reads-the-database
---

# 4.6 Two orders, one T-shirt

The concurrency check turns a lost race into a `DbUpdateConcurrencyException`. Running the same
order again would read the new stock and could succeed, so it is a transient failure.

`Failures/ShopfrontFailureClassifier.cs`:

```csharp compile=app file=Failures/ShopfrontFailureClassifier.cs
using Microsoft.EntityFrameworkCore;
using Trax.Core.Exceptions;
using Trax.Effect.Services.FailureClassifier;

namespace Shopfront.Failures;

public class ShopfrontFailureClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception switch
        {
            // A message for the customer: the same order fails the same way however often it runs.
            TrainException => FailureClass.Permanent,
            // The provider did not answer this time; it may next time.
            TimeoutException => FailureClass.Transient,
            // Another order changed the stock first; trying again reads the new stock.
            DbUpdateConcurrencyException => FailureClass.Transient,
            _ => null,
        };
}
```

## SDK Reference

> [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context) | [EnsureSchemaCreatedAsync](/docs/effect/domain-schema-bootstrap) | [Add a domain data context](/docs/effect/effect-providers/domain-data-contexts) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Architecture guards](/docs/reference/architecture-guards)

---

Next: [4.7 Register the context and create its schema](/docs/book/the-shops-own-data/register-the-context-and-create-its-schema)
