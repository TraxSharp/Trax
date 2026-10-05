# GraphQLClient: a Trax server and the Trax GraphQL client together

Calling one GraphQL server from .NET code with `Trax.Api.GraphQL.Client`, whose requests are
validated against the server's schema before anything is sent.

What it proves:

- **One consumer, two Trax servers** (`Gateway`): two keyed clients in one container
  (`AddKeyedTraxGraphQLClient("serverB", ...)`, `"serverC"`), each validating against and querying
  its own server. A query written for the other server is refused with `GraphQLValidationException`
  before any HTTP call. The servers are ordinary Trax hosts (`[TraxQuery(Namespace = ...)]` trains,
  in-memory effects) and the typed requests reach them through `Path = "discover.<namespace>"`.
- **Three ways to write a request** (`Trax.Samples.GraphQLClient`): a raw query string (mode A), a
  `.graphql` embedded resource (mode E), and a typed POCO whose shape is the selection set (mode D),
  all returning the same player. The client validates against the server's schema configuration
  itself (`UseAssemblySchema`), so the two cannot drift.

| Project | Role |
|---|---|
| `Trax.Samples.GraphQLClient.InventoryServer` | Trax server: `discover { inventory { getProduct } }` |
| `Trax.Samples.GraphQLClient.BillingServer` | Trax server: `discover { billing { getInvoice } }` |
| `Trax.Samples.GraphQLClient.Gateway` | Starts both servers in-process, then calls each through its keyed client |
| `Trax.Samples.GraphQLClient` | A HotChocolate server and a client calling it in modes A, E and D |

## Run

From `Trax.Samples/` (no database or broker needed):

```bash
dotnet run --project samples/GraphQLClient/Trax.Samples.GraphQLClient.Gateway
dotnet run --project samples/GraphQLClient/Trax.Samples.GraphQLClient
```

The Gateway starts both servers in-process, on 5310 and 5311. Either server also runs on its own,
in Development, on 5313 (inventory) or 5314 (billing):

```bash
dotnet run --project samples/GraphQLClient/Trax.Samples.GraphQLClient.InventoryServer
```

## Try it

The Gateway prints:

```text
serverB (inventory) -> Mechanical Keyboard, 42 on hand
serverC (billing)   -> invoice INV-1: 4999c (Paid)
serverB rejected a billing query (schema isolation holds)
```

The modes sample serves its schema on `http://localhost:5312` while it runs, and ends with
`A == E (raw vs resource) : True` and `A ~ D (raw vs typed) : True`. If the modes disagree it says
so and exits with 1.

## Tests

```bash
dotnet test tests/Trax.Samples.GraphQLClient.E2E
```

The suite boots both Trax servers with `WebApplicationFactory` and points two keyed clients at them.

Docs: <https://traxsharp.net/docs/samples/graphql-client>
