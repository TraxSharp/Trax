# Trax Persisted Operations Sample

A GraphQL API that accepts only persisted operations: a client sends an operation id and its
variables, and the server runs the document it stores for that id. An operator can rewrite the
stored document without shipping a new client, and the shape-diff guardrail refuses any edit that
would change the response shipped clients read.

## What it proves

- `UsePersistedOperations(...).RequirePersisted(true).SingleNode()` rejects every inline document
  with `PERSISTED_OPERATION_REQUIRED`, on every transport.
- The management mutations under `operations.persistedOperations` are gated to the `Operator` role
  with `GateOperations(roles: "Operator")`; an anonymous upload is refused.
- A shape-preserving edit takes effect on the next request by the same id; a shape-changing edit is
  refused with `SHAPE_DIFF_VIOLATION`.
- The demo key, the `dev_` allowlist and the dashboard exist only in Development.

## Run

```bash
# From the Trax.Samples root: Postgres on localhost:5432 (database trax_persisted_operations)
docker compose up -d

# The API, in Development, on http://localhost:5240
dotnet run --project samples/PersistedOperations/Trax.Samples.PersistedOperations.Api

# In a second terminal: upload the manifest, call by id, hot-fix, hit the guardrail
dotnet run --project samples/PersistedOperations/Trax.Samples.PersistedOperations.Client
```

## Try it

```bash
# Inline documents are refused
curl -s http://localhost:5240/trax/graphql/ -H 'Content-Type: application/json' \
  -d '{"query":"{ discover { greeting { greet(input: {name: \"Eve\"}) { greeting } } } }"}'
# {"errors":[{"message":"Only persisted operations are accepted on this server.","extensions":{"code":"PERSISTED_OPERATION_REQUIRED"}}]}

# After the client has run, the same call by id works
curl -s http://localhost:5240/trax/graphql/ -H 'Content-Type: application/json' \
  -d '{"id":"greet_v1","variables":{"input":{"name":"Eve"}}}'
```

The dashboard's **Data > Persisted Operations** page is at http://localhost:5240/trax.

## Tests

```bash
dotnet test tests/Trax.Samples.PersistedOperations.E2E
```

The suite runs against the `persisted_operations_e2e_tests` database on port 5432
(`TRAX_TEST_PG_PORT` moves the port). It fails, rather than skips, when that database is missing.

## Docs

[Persisted Operations sample](https://traxsharp.net/docs/samples/persisted-operations) and
[Persisted Operations](https://traxsharp.net/docs/persisted-operations).
