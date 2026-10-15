# Trax Persisted Operations Sample

A GraphQL API that accepts only persisted operations: an app sends an operation id and its
variables, and the server runs the document it stores for that id. The sample tells three stories:

1. **A shipped app calls the API by id.** The app sends `{ "id": "greet_v1", "variables": { "name": "Alice" } }`
   and gets `Hey Alice!`. An inline query is refused.
2. **Fix it without shipping a new app.** The stored `greet_v1` asks for `style: CASUAL`. The
   operator re-uploads it with `style: FORMAL`, and the same unchanged call now answers
   `Good day, Alice.`. The response shape is the same, so the upload needs no bypass.
3. **A fix that would break shipped apps is refused.** An upload that renames the field the app
   reads (`message: greeting`) fails the shape-diff guardrail, and `greet_v1` keeps its answer.

## What it proves

- `UsePersistedOperations(...).RequirePersisted(true).SingleNode()` rejects every inline document
  with `PERSISTED_OPERATION_REQUIRED`.
- The management mutations under `persistedOperations` are gated to the `Operator` role with
  `GateManagement(roles: "Operator")`; an anonymous upload is refused with `TRAX_AUTHORIZATION`.
- An upload that keeps the response shape takes effect on the next call by the same id; one that
  changes it is refused with `SHAPE_DIFF_VIOLATION`.
- The demo operator key and the dashboard exist only in Development.

## Run

```bash
# From the Trax.Samples root: Postgres on localhost:5432 (database trax_persisted_operations)
docker compose up -d

# The API, in Development, on http://localhost:5240
dotnet run --project samples/PersistedOperations/Trax.Samples.PersistedOperations.Api

# In a second terminal: the three stories
dotnet run --project samples/PersistedOperations/Trax.Samples.PersistedOperations.Client
```

The client uploads `Client/manifest.json` first, which puts `greet_v1` back to `CASUAL`, so it can
run any number of times.

Or open http://localhost:5240 and play the same stories in a page: each request it sends, the answer
the app shows, and what the server stores for `greet_v1`. The page is served only in Development,
because the operator's uploads send the demo key.

## Try it

```bash
# An inline document is refused
curl -s http://localhost:5240/trax/graphql/ -H 'Content-Type: application/json' \
  -d '{"query":"query { discover { greeting { greet(input: { name: \"Eve\", style: FORMAL }) { greeting } } } }"}'
# {"errors":[{"message":"Only persisted operations are accepted on this server.","extensions":{"code":"PERSISTED_OPERATION_REQUIRED"}}]}

# After the client has uploaded the manifest, the call by id works
curl -s http://localhost:5240/trax/graphql/ -H 'Content-Type: application/json' \
  -d '{"id":"greet_v1","variables":{"name":"Eve"}}'
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
