---
title: "8.5 Open it"
description: "Chapter 8 of the Trax book, section 5: Open it."
type: tutorial
area: general
parent: "8. The operations console"
grand_parent: The Trax book
nav_order: 5
snippet_context: none
follows: book/the-operations-console/who-may-load-the-page
---

# 8.5 Open it

```bash
dotnet run --urls http://localhost:5080
```

Mint a token for a member of staff and copy it:

```bash
curl -s 'http://localhost:5080/dev/token/lin?staff=true'
```

Open `http://localhost:5080/trax/dashboard` and paste the token when the console asks for a
credential. The console keeps it in the page's memory only: reload the page and it asks again.

**Executions** lists every run, newest first, with its state and, for a failed one, the junction
and the reason. These are the rows `psql` showed in chapter 3. Open a failed order from chapter 3,
the declined card:

- **Input** and **Output** are the stored JSON. The receipt link on a completed order's output is
  `{"_redacted": true}`, as `[TraxSensitive]` made it.
- **Failure** names `TakePaymentJunction`, the message, and the class, `Permanent`.
- **Steps** is the run's junctions in order, each with its timing, the step that failed marked.
- **Decisions** shows the fraud check of every run since chapter 5: the live rule's answer, the
  shadow's, and whether they agreed.

**Trains** lists `PlaceOrder` with its input type and the attributes on it. **Cluster** lists the
process that ran each run.

Leave the console open on **Executions** and place an order as a customer. It appears at the top
as it runs.

## SDK Reference

> [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Queries](/docs/sdk-reference/graphql-api/queries)

---

Next: [8.6 A customer gets nothing](/docs/book/the-operations-console/a-customer-gets-nothing)
