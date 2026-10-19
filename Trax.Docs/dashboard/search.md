---
title: Search
description: "Reference for the operations console's search: finding a run, manifest, queue entry, dead letter, machine instance or train by an exact id, key or name."
type: reference
area: dashboard
nav_order: 12
source:
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/components/Search.tsx
---

# Search

The box in the header of every page, **Find by id, external id, subject or name**. Press `/`
anywhere outside a text field to focus it. It reads
[`operations.lookup`](/docs/sdk-reference/graphql-api/queries/operations#lookup).

Matches are exact, after trimming; part of a name finds nothing.

| Finds | By |
|---|---|
| A run | Its id or external id |
| A manifest | Its id, external id or name |
| A queued entry | Its id, external id or subject key, while it is queued or dispatched |
| A dead letter | Its id |
| A machine instance | Its id |
| A train | Its full or short name |

One match opens its page. Several are listed by kind, at most 10 of each, and clicking one opens it.
"Nothing has that id, key or name." when there is none. An instance is named as the Machines pages
name it, never by its context or by whose a user's draft is.
