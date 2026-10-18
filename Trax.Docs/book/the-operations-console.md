---
title: "8. The operations console"
description: "Chapter 8 of the Trax book: open the operations console as a member of staff to read every run, its failure and its decisions, behind the operations gate."
type: tutorial
area: general
parent: The Trax book
nav_order: 18
---

# 8. The operations console

Every run of `PlaceOrder` is recorded with its input, output, failure and decisions, and so far the
only way to read any of it has been `psql`. In this chapter Shopfront serves the operations
console: a page where a member of staff reads every run, opens a failed one to see which junction
threw and why, and follows runs as they happen. Customers cannot reach any of it.

This chapter starts from the end of [chapter 7](/docs/book/serve-it-over-graphql).

## In this chapter

1. [The console reads through GraphQL](/docs/book/the-operations-console/the-console-reads-through-graphql)
2. [Packages](/docs/book/the-operations-console/packages)
3. [Open the namespace to staff, and serve the console](/docs/book/the-operations-console/open-the-namespace-to-staff-and-serve-the-console)
4. [Who may load the page](/docs/book/the-operations-console/who-may-load-the-page)
5. [Open it](/docs/book/the-operations-console/open-it)
6. [A customer gets nothing](/docs/book/the-operations-console/a-customer-gets-nothing)
7. [Test it](/docs/book/the-operations-console/test-it)

## SDK Reference

> [UseTraxApiDashboard](/docs/sdk-reference/api-dashboard/use-trax-api-dashboard) | [AddTraxGraphQL](/docs/sdk-reference/graphql-api/add-trax-graphql) | [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Queries](/docs/sdk-reference/graphql-api/queries)
