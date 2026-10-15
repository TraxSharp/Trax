---
authors: [Theauxm]
areas: [platform, ui]
status: accepted
---

# The persisted-operations pages call the shared persisted-operations service

The persisted-operations pages read and write through `IPersistedOperationsService` from
`Trax.Api.GraphQL.PersistedOperations`, the service the API's `persistedOperations`
fields call, resolved from the host's container. They do not construct the API's resolver classes,
call `IPersistedOperationStore` directly, or query `trax.persisted_operation` themselves. That is
how they meet central ADR 0022 (one shared path per action) for an action the Scheduler's
`IOperationsService` does not own.

They are shown only once the host declares the management gate (`GateManagement`, on
`UsePersistedOperations` or on `AddPersistedOperationStore`), and every read and write checks it
for the operator through `IPersistedOperationsManagementGate`, after the dashboard's own posture.
The API's `persistedOperations` fields carry the same requirements, so the Blazor dashboard, the
React dashboard and GraphQL refuse the same people.

## Status

**Accepted.** Supersedes [0004](./0004-persisted-operations-pages-call-the-api-resolvers.md),
which had the pages construct the API's resolvers and hand them the store, because the service
did not exist yet. 0004 named the service as the end state to move to; this is that move.

## Considered options

**Gate the pages on the dashboard's posture alone.** Rejected. A host that manages documents out
of band declares no management gate and serves no management fields, and the React dashboard,
which calls those fields, could not manage them; the Blazor pages could, for any dashboard
operator. That broke parity.

**Keep calling the resolvers.** Rejected. The resolver overloads that take a store are kept by
the API package only for old callers and are hidden from new ones. Calling them builds a service
around whatever store the page was given, so the page bypasses the host's registration: the
broadcaster `AddPersistedOperationStore(conn, rabbitMqConn)` wires up, and anything else the
service gains later, never runs for a dashboard write.

**Gate the pages on `IPersistedOperationsCapability`, as before.** Rejected. Only
`UsePersistedOperations` registers that marker, so a dashboard in a process that serves no
GraphQL, which registers the store with `AddPersistedOperationStore`, never showed the pages even
though everything they need was there. Both registrations register the service, so the pages and
the sidebar gate on the service and the declared management gate. The marker still means "this process serves the management
GraphQL fields", which the dashboard does not need to know.

## Consequences

The dashboard reports what the API reports, because it runs the same code: an upload with no id
says "id is required.", a deactivation with no reason is refused, and a deactivation the API
would answer with an error comes back with the API's message. A persisted operation is still
addressed by tenant and id, and the list still reads at most 200 rows a request.

A dashboard in its own process declares how its store reaches the GraphQL nodes:
`AddPersistedOperationStore(store => store.UseRabbitMqInvalidation(...))` with the broker they
use, or `store.SingleNode()` when no other process caches the operations. The store refuses to
start with neither. That is the API package's rule, and the dashboard follows it instead of
opting out. A change the store saved but could not broadcast comes back as
`CHANGE_NOT_BROADCAST` beside the saved row; the pages show it as a warning with the API's
message, not as "not changed", because the change is the operation's new state.

## Exemplars

- `PersistedOperationsTenantTests` pins the pages to the service on a host that registers only
  `AddPersistedOperationStore`: the detail page reads and deactivates through the registered
  service, a refused deactivation is reported with the API's own message, rows sharing an id open
  and change only themselves, and an upload is refused with the API's message.
- `PersistedOperationNotBroadcastTests` pins the warning: a deactivation or an upload the store
  saved but could not broadcast is reported as saved with the API's message, not refused.
- `PersistedOperationsSidebarTests` pins the link: it shows with the service and a declared
  management gate, without the capability marker, and hides with the marker alone or with no gate.
- `PersistedOperationsTenantTests` also pins the management gate: with none declared the dashboard
  cannot upload, and an operator who meets the dashboard's posture but not the gate's policy is
  refused until they do.

Not covered: nothing stops a future page from querying the table or the store directly again. The
guard is the tests above, which fail only for the behaviour they exercise.

## Changelog

- **2026-10-15**: The pages show and act only behind the declared management gate, checked for the
  operator as GraphQL checks it.
- **2026-10-05**: The store declares how it reaches the other nodes (the single-argument
  overload no longer compiles), and a change saved but not broadcast is shown as a warning.
- **2026-10-01**: Recorded.
