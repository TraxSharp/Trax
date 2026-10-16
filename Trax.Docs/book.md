---
title: The Trax book
description: "A tutorial series in chapters that builds one shop with Trax, from its first train to a React storefront over a typed generated client."
type: landing
area: general
section: Tutorials
nav_order: 2
---

# The Trax book

The book builds one application, a small shop called Shopfront, from an empty folder to a deployed
API with a React storefront. Each chapter starts from where the last one stopped, adds one part of
Trax, and ends with something you can run and look at. Read it in order.

The [quick start](/docs/getting-started) is the short version: one train, recorded, scheduled and
served in an afternoon. The book covers the same ground more slowly and then keeps going.

Every C# block in the book compiles in CI against the release the site was published from, as in the
quick start. Chapters without a link are not written yet.

## What we build

Shopfront sells a small catalogue. Customers browse it, put items in a cart, check out and follow
their order. Staff manage stock and see every order. Behind that:

- placing an order reserves stock, takes a payment from a fake payment provider that sometimes
  fails, and confirms the order;
- a nightly job writes a stock report, and an hourly one releases carts nobody finished;
- fulfilment runs on a separate worker process;
- checkout is a state machine the browser and the server share;
- the storefront is a React app over a client generated from the API.

## Part 1: Trains

### [1. An order is a train](/docs/book/an-order-is-a-train)

Create the project and write `PlaceOrder` as a train of small junctions: validate the cart, reserve
stock, take the payment, confirm. Run it in-process with in-memory storage and print what each
junction did.

You learn: trains, junctions, Memory, how one junction's output becomes a later one's input, and
the check that refuses to start an application whose chain cannot run.

### 2. Every order is recorded

Move to Postgres. Every run of `PlaceOrder` is now a row with its input, output, timing and, when it
fails, the junction and exception. Make the payment provider fail one call in five and read the
failed runs back.

You learn: the effect system, metadata, junction logging, and masking the card number with
`[TraxSensitive]` so it is never stored.

## Part 2: Running it for real

### 3. Scheduled work

Add the nightly stock report and the hourly cart cleanup. Open the dashboard and watch both run,
then skip the report on public holidays.

You learn: the scheduler, cron and interval schedules, exclusion windows, misfire policies, and
the operations dashboard.

### 4. Failures, retries and dead letters

Queue `PlaceOrder` instead of running it inline, and give it a retry policy so a failed payment is tried again with backoff. Exhaust the
retries and find the order among the dead letters, then requeue it after fixing the provider.

You learn: failure classes, retry backoff, dead letters, and requeueing a run with the input it
was given.

### 5. A separate worker

Move fulfilment out of the API into its own worker process that takes queued runs. Cap it to two
fulfilments at a time, kill it mid-run and watch the run come back.

You learn: queueing, workers, per-train concurrency limits, and what happens to a run whose worker
dies.

## Part 3: The API

### 6. Serve it over GraphQL

Expose the catalogue as queries and `PlaceOrder` as a typed mutation. Follow an order's progress
over a subscription.

You learn: `[TraxQuery]` and `[TraxMutation]`, query models with filtering, sorting and paging, and
lifecycle subscriptions.

### 7. Customers and staff

Add sign-in. A customer reads only their own orders and a member of staff reads all of them.
Switch the API to persisted operations so it accepts no query text from the browser.

You learn: JWT authentication, `[TraxAuthorize]` on trains and on query models, owner-scoped rows,
and persisted operations.

## Part 4: Machines

### 8. Checkout is a state machine

Rewrite checkout as a machine: cart, address, payment, placed. Each move is a typed mutation, the
server applies it, and an abandoned checkout times out on its own.

You learn: authoring a machine, guards, timers, an authorization rule on every event, and serving
a machine as its own API.

### 9. The machine runs the order

When checkout reaches placed, the machine invokes `PlaceOrder` and moves on with its result:
confirmed, failed or cancelled.

You learn: invoking a train from a state, and routing a train's outcome back into the machine.

## Part 5: Composition

### 10. Checks in parallel, and resuming after a crash

Run the fraud check, the stock check and the price check side by side. Put a checkpoint after the
payment, so a crash after it resumes there instead of charging the customer twice.

You learn: `Parallel` and its branches, checkpoints, and resuming a run from the dashboard and
from GraphQL.

## Part 6: The storefront

### 11. A generated client

Export the client manifest from the host and generate a typed React client with `trax client`.
Write the documents the catalogue page and the order page send, and build both pages on the functions `trax client`
generates from them.

You learn: the client manifest, `.graphql` documents and `trax client --documents`, typed functions, and `--check`
in CI.

### 12. Checkout in the browser

Drive checkout from React with the generated hooks. The browser predicts each move with the
machine's twin and the server confirms it. Show an order's progress while `PlaceOrder` runs.

You learn: the TypeScript twin, the generated machine hooks, and following a run with
`useRunProgress`.

## Part 7: Shipping it

### 13. Deploy

Split Shopfront into the API, the scheduler and the worker, put them in containers, and point them
at one Postgres. Secure the dashboard.

You learn: the [deployment topologies](/docs/reference/deployment-topologies), migrations at
startup, and what each process needs.

## Where to go next

- [Samples](/docs/samples): complete applications that use the same pieces in other shapes.
