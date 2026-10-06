---
authors: [Theauxm]
areas: [platform, ui]
status: superseded-by-0005
---

# The persisted-operations pages call the API package's resolvers

The persisted-operations pages read and write through the public resolver classes in
`Trax.Api.GraphQL.PersistedOperations` (`PersistedOperationQueries` and
`PersistedOperationMutations`), constructed in the page and handed the host's
`IPersistedOperationStore` and `IDataContextProviderFactory`. They do not query
`trax.persisted_operation` themselves or call the store directly. That is how they meet
central ADR 0022 (one shared path per action) for an action the Scheduler's
`IOperationsService` does not own.

## Status

**Superseded by** [0005](./0005-persisted-operations-pages-call-the-shared-service.md). The
persisted-operations service this ADR named as the end state was released, and the pages now call
it instead of the resolvers.

## Why this is written down

Because constructing GraphQL resolver classes in a Blazor page looks like an accident. It is
not. The pages had their own copies: a list capped at 500 rows across every tenant, a detail
page that looked up only the default tenant, and writes that passed no tenant and skipped
the API's input checks. A row in a named tenant listed but opened as "not found", and a
default row sharing its id was the one edited. The resolvers already hold the tenant rule,
the page bounds and the input checks, and they are public and stateless.

## Considered options

**Keep the dashboard's own queries, made tenant-aware.** Rejected: it fixes the tenant bug
and leaves two implementations that ADR 0022 counts as a defect, and the next change to one
of them drifts again.

**A persisted-operations service in the API package that both surfaces call.** The better
end state, and the one to move to. It needs a Trax.Api change and release first; until then
the resolvers are that service in all but name, and switching to it later is a change to
three pages.

## Consequences

The dashboard now reports what the API reports. An upload with no id says "id is required.",
a deactivation with no reason is refused by the resolver, and a deactivation of a row
that is already inactive comes back not found, as it does from the API. The list pages at
most 200 rows a request, the resolver's bound, rather than holding every row in the circuit.

A persisted operation is addressed by tenant and id: the detail page takes the tenant as
`?tenant=`, absent meaning the default tenant, and the list links each row with its own.

## Exemplars

**Unenforced:** superseded by 0005, whose exemplars now hold the tests that pinned this one;
the pages no longer construct the resolvers, so no test here could fail for this decision.

## Changelog

- **2026-10-01**: Superseded by
  [0005](./0005-persisted-operations-pages-call-the-shared-service.md); its exemplar moved there.
- **2026-09-27**: Recorded.
