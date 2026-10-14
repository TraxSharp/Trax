---
title: Dashboard API
description: "Index of the Trax.Dashboard reference: registration and mounting, the options and posture, per-browser settings, and the component model types."
type: landing
area: dashboard
parent: SDK Reference
has_children: true
nav_order: 7
---

# Dashboard API

Reference for `Trax.Dashboard`, the Blazor Server UI shipped as a Razor Class Library and mounted
into an ASP.NET Core host. For what it shows, see [Dashboard](/docs/dashboard).

## Trax.Dashboard

- [AddTraxDashboard](/docs/sdk-reference/dashboard-api/add-trax-dashboard): registers the dashboard services.
- [UseTraxDashboard](/docs/sdk-reference/dashboard-api/use-trax-dashboard): maps the dashboard at `/trax` behind its authorization posture.
- [DashboardOptions](/docs/sdk-reference/dashboard-api/dashboard-options): the title and the authorization posture.
- [IDashboardSettingsService](/docs/sdk-reference/dashboard-api/i-dashboard-settings-service): per-browser preferences and refresh state.
- [Dashboard component model types](/docs/sdk-reference/dashboard-api/component-model-types): `ServerDataResult`, `DagLayout`, `PositionedNode`, `PositionedEdge`.
