---
title: Views root
description: "Reference for the views GraphQL root: one query field per view a host serves from features, read only by the view's readers."
type: reference
area: api
surface: graphql
package: Trax.Api.GraphQL
parent: GraphQL API
grand_parent: SDK Reference
nav_order: 14
source:
  - Trax.Api/src/Trax.Api.GraphQL/TypeModules/MachineViewTypeModule.cs
  - Trax.StateMachine/dotnet/src/Trax.StateMachine.Persistence/FromIr/MachineViews.cs
  - Trax.Api/src/Trax.Api.GraphQL.PersistedOperations/Services/ViewDocuments.cs
---

# Views root

A host that serves machines from features
([`ServeFeatures`](/docs/sdk-reference/statemachine-api/add-state-machines)) serves each view its `host.json` declares
as a query field under `views`. A view shows fields of the machines it reads: the caller's own instances of a user
machine, and the one instance of a system machine at its key. Who may read a view is its readers, the actors the
features show reading it ([feature files](/docs/reference/feature-files)). The decision is
[ADR 0058](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0058-features-and-a-glossary-are-a-source-of-machines.md).

## Members

| Field | Type | Answers |
| --- | --- | --- |
| `views` | `ViewsQueries!` | The views, on the query root only. There is no `views` field on a host that registers none. |
| `views.<view>` | `<View>View!` | The view, for the caller. The field is the view's name in camelCase (`shop sign` is `shopSign`), its type the name in PascalCase with `View` after it (`ShopSignView`). |
| `views.<view>.<machine>` | `[<View><Machine>Row!]!` | For a user machine: the caller's own instances, oldest first. The field is the machine's field under `machines` (`v-order` is `vOrder`). |
| `views.<view>.<machine>` | `<View><Machine>Row` | For a system machine: its instance at the view's key, or null before the host has started it. |
| `<row>.id` | `UUID!` | The instance's id. |
| `<row>.<field>` | see below | One field per field the view shows of that machine, under the view's name for it. |

A field `from` a machine's `state` is a `String`: the state's label when the view gives `labels` for it, the state's
name otherwise. A field `from` a context field has the GraphQL type the machine's record gives that field, as on the
machine's own operations ([Machines root](/docs/sdk-reference/graphql-api/machines)).

## Example

```graphql
query {
  views {
    orders { vOrder { id status loaves } }
    shopSign { vShop { open } }
  }
}
```

## Behavior

- **Who reads.** A caller with no user key is refused with `UNAUTHENTICATED`. A view whose readers are not anyone
  signed in is read by a caller in one role of every role set they name; anyone else is refused with `FORBIDDEN`
  before anything is read. Both are GraphQL errors whose `extensions.code` is the code.
- **What is read.** Only the caller's own instances (owner `user`, with the caller's user key), and the system
  instance at the key the view names. Another user's instance is never in a view.
- **Names.** A view's name is words of letters and digits, the first starting with a letter. A field it shows is a
  GraphQL name (letters, digits and `_`, not starting with a digit or `__`) and is not `id`, which is the instance's.
  Any other name refuses the host with an `InvalidOperationException` naming the view and the field when the schema
  builds.
- **Registration order.** The machines directory, and with it the views, are registered before `AddTraxGraphQL()`.
  A host that registers views after it has no `views` root, and refuses to start with an `InvalidOperationException`
  naming them ([registration order](/docs/reference/registration-order)).

### Persisted documents

With [persisted operations](/docs/sdk-reference/persisted-operations/use-persisted-operations) on, the host
registers one document per view, selecting every field the view shows, so a host that requires persisted operations
serves its views with no upload. The id is `trax_view_<view>_<shapeHash><textHash>`, `<view>` being the view's field
under `views`, hashed as a train's documents are: a client built against one shape of a view is told
`CLIENT_OUTDATED` once the shape changes. The documents live under the null tenant, and the `trax_view_` prefix is
reserved: upload, deactivate and restore refuse it (`RESERVED_ID`). The view's readers still decide who reads it;
the document only lets the host run it. A generated client finds each view's document under `Views` in the
[client manifest](/docs/sdk-reference/statemachine-api/client-manifest).

## See also

- [Machines root](/docs/sdk-reference/graphql-api/machines): the machines a view reads, served as typed operations.
- [Feature files](/docs/reference/feature-files): where a view and its readers come from.
- [Error codes](/docs/sdk-reference/graphql-api/error-codes): `UNAUTHENTICATED`, `FORBIDDEN`, `CLIENT_OUTDATED`.
