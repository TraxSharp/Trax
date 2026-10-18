---
title: "4.1 One context, one schema"
description: "Chapter 4 of the Trax book, section 1: One context, one schema."
type: tutorial
area: general
parent: "4. The shop's own data"
grand_parent: The Trax book
nav_order: 1
snippet_context: none
follows: book/every-order-is-recorded/test-it
---

# 4.1 One context, one schema

Trax's own tables belong to Trax's data context. An application's data goes in a context of its
own, derived from `DomainDataContext<TSelf>`, which holds to one rule: one context, one Postgres
schema. Code that uses the data depends on an interface beside the context, never on the context
itself.

By the end of the chapter the new files are:

```text
Shopfront/
└── Data/
    ├── IShopfrontDbContext.cs
    ├── ShopfrontDbContext.cs
    └── Models/
        ├── Product.cs
        ├── Order.cs
        └── OrderItem.cs
```

## SDK Reference

> [DomainDataContext](/docs/sdk-reference/configuration/domain-data-context) | [EnsureSchemaCreatedAsync](/docs/effect/domain-schema-bootstrap) | [Add a domain data context](/docs/effect/effect-providers/domain-data-contexts) | [IFailureClassifier](/docs/sdk-reference/configuration/i-failure-classifier) | [Architecture guards](/docs/reference/architecture-guards)

---

Next: [4.2 Packages](/docs/book/the-shops-own-data/packages)
