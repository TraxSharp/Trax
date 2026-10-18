---
title: "15.8 Keep it current"
description: "Chapter 15 of the Trax book, section 8: Keep it current."
type: tutorial
area: general
parent: "15. A generated client"
grand_parent: The Trax book
nav_order: 8
snippet_context: none
follows: book/a-generated-client/run-it
---

# 15.8 Keep it current

The client is generated from the hub, so any change to a train, a query model, a machine or a
document means exporting and generating again. Forgetting to is caught in CI: `--check` writes
nothing and fails, naming each file, when what is committed is not what the manifest generates.

```bash
dotnet run --project Shopfront.Hub -- --trax-export-client ../web/src/trax/manifest.json
cd web
dotnet trax client --manifest src/trax/manifest.json --out src/trax --documents 'src/graphql/*.graphql' --check
```

```text
The client in /home/you/Shopfront/web/src/trax is what the manifest generates.
```

Deploy the hub before the storefront: a page whose documents the hub has not registered yet is
refused.

## SDK Reference

> [Generated clients](/docs/statemachine/call-a-machine-from-typescript) | [trax client](/docs/reference/cli) | [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest) | [Fetch only what a component needs](/docs/api/fetch-only-what-a-component-needs) | [Run progress](/docs/sdk-reference/graphql-api/run-progress)

---

Next: [15.9 Test it](/docs/book/a-generated-client/test-it)
