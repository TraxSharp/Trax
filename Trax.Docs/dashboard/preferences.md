---
title: Preferences
description: "Reference for the operations console's Preferences page: theme, credential, notifications, refresh, scheduler trains and Overview panels."
type: reference
area: dashboard
nav_order: 11
source:
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/UserSettingsPage.tsx
redirect_from:
  - /docs/dashboard/store-dashboard-settings-per-user
---

# Preferences

`/preferences`, at the foot of the sidebar. Each preference is kept in this browser's
`localStorage`, for this browser only; nothing is stored on the server, and another browser starts
from the defaults.

| Panel | Setting | Default |
|---|---|---|
| **Appearance** | Light or dark theme; the sidebar has the same toggle | The operating system's preference |
| **Connection** | How the console authenticates (an API key or a bearer token), the credential masked, and **Change credential**, which forgets it and asks again. Not shown on a host that signs the browser in. | |
| **Notifications** | A desktop notification whenever a train fails, after the browser grants permission | Off |
| **Refresh** | **Refresh this page every** N seconds. The lists already update live over the subscription; this reads the open page again on a fixed interval in case the socket drops. 0 turns it off. | 0 |
| **Scheduler trains** | **Hide scheduler trains**: leaves the scheduler's own trains (the job dispatcher, the manifest manager, the job runner and the cleanups) out of the run lists, the live feed, the Overview, Trains and Manifests. The same switch sits on those pages. A filter that names one of those trains still shows it. | On |
| **Overview panels** | Which panels of the [Overview](/docs/dashboard/overview) show; **Reset default** shows them all | All shown |

The credential itself is never stored: the console holds it in memory, so it is asked for again
after every reload ([How the app authenticates](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard#how-the-app-authenticates)).
How often the scheduler checks for work is a scheduler setting, on [System](/docs/dashboard/system#scheduler).
