---
title: API admin
description: "Reference for the operations console's API admin area: the persisted operations pages, shown to a caller the host lets manage persisted GraphQL documents."
type: reference
area: dashboard
nav_order: 10
source:
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/PersistedOperationsPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/PersistedOperationDetailPage.tsx
---

# API admin

Shown only when the host serves [persisted operations](/docs/persisted-operations) management to
this caller: it turns on persisted operations, declares the management gate (`GateManagement`), and
the caller meets it. Every read and write goes through the `persistedOperations` fields, behind that
gate, so the console refuses what the API refuses, with the same message. Turning it on is in
[Turn on persisted operations](/docs/api/turn-on-persisted-operations).

## Persisted operations

`/api-admin/persisted-operations`. The stored GraphQL documents a page at a time, filtered by
**Tenant** (with a **Tenant key**), **Status** and **Id starts with**. **Upload** opens the editor
for a new document.

## A persisted operation

`/api-admin/persisted-operations/{id}`, with `?tenant={key}` for a tenant other than the default.
Its tenant, operation name and version, whether it is active (and why it was deprecated), its shape
fingerprint, description, and when it was created and updated; its **Document**; and its
**History**.

| Action | Does |
|---|---|
| **Edit** | Opens the editor on its document. Parse, schema and shape errors show in the editor. **Bypass shape-diff guardrail** sends the change even when its shape differs from the stored one; use it only when the change is known to be safe for shipped clients. |
| **Deactivate** | Asks for a reason and deactivates it |
| **Restore** | Makes a deactivated operation active again |

The console's own documents are registered on a host that runs persisted operations, so it keeps
working on a host that requires them; see
[UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard#with-persisted-operations).
