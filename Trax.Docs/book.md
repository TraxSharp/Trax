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
quick start, and each chapter compiles as the whole project so far. Chapters without a link are not
written yet.

## What we build

Shopfront sells a small catalogue. Customers browse it, check out and follow their order. Staff
manage stock and see every order. Behind that:

- placing an order prices the cart, reserves stock, takes a payment from a fake provider that
  sometimes fails, and confirms the order;
- customers sign in and see only their own orders, and staff see every run in the operations
  console;
- a nightly job writes a stock report, and a supplier sync is retried when the supplier is down;
- fulfilment runs on a separate worker process;
- checkout is a state machine the browser and the server share;
- the storefront is a React app over a client generated from the API.

## Part 1: Trains

### [1. An order is a train](/docs/book/an-order-is-a-train)

Create the project and write `PlaceOrder` as a train of small junctions: price the cart, reserve
stock, take the payment, confirm. Run it in-process with in-memory storage and print what each
junction did.

You learn: trains, junctions, Memory, `TrainException`, cancellation, and the check that refuses to
start an application whose chain cannot run.

### [2. Testing PlaceOrder](/docs/book/testing-place-order)

Add a test project. Test each junction on its own, the whole train through the train bus, and the
host as a client calls it.

You learn: unit tests for internal junctions, train tests on in-memory storage, host tests, and
the architecture guards.

### [3. Every order is recorded](/docs/book/every-order-is-recorded)

Move to Postgres. Every run of `PlaceOrder` is now a row with its input, output, timing and, when it
fails, the junction and exception. Make the payment provider fail one call in five, give the stock
back when it does, and read the failed runs back.

You learn: the effect system, metadata, junction logging, failure classes, and keeping a secret
out of the record with `[TraxSensitive]`.

### [4. The shop's own data](/docs/book/the-shops-own-data)

Give Shopfront a database of its own beside Trax's: products with their stock, and the orders
placed against them. Reserve stock in a transaction, and store each confirmed order.

You learn: domain data contexts, their companion interfaces, creating their schema at startup,
and testing against a real database.

### [5. Does this order look like fraud](/docs/book/does-this-order-look-like-fraud)

Stop an order that looks like fraud before it is charged, with a rule the train asks rather than
an `if` inside a junction. Then try a second rule beside the first without letting it decide.

You learn: decisions, deciders, decision recording, shadow deciders, and testing a decision.

## Part 2: The API

### [6. Customers and staff](/docs/book/customers-and-staff)

Add sign-in. A customer places orders as themselves and reads only their own orders, and a member
of staff reads all of them.

You learn: authentication, `[TraxAuthorize]`, taking the caller from the principal rather than the
input, owner-scoped rows, and the startup checks that refuse an unguarded train.

### [7. Serve it over GraphQL](/docs/book/serve-it-over-graphql)

Expose the catalogue to everyone, `PlaceOrder` to customers, and the orders to their owners.
Follow an order's progress while it runs.

You learn: `[TraxQuery]` and `[TraxMutation]`, query models, and following a run with
`[TraxRunProgress]`.

### [8. The operations console](/docs/book/the-operations-console)

Open the operations console as a member of staff: every run, its input and output, the junction
that failed and the decisions it made.

You learn: the operations namespace, gating it to a role, and the console.

## Part 3: Running it for real

### [9. Scheduled work](/docs/book/scheduled-work)

Add the nightly stock report and a low-stock check that runs after it. Watch both in the console,
then skip the report on public holidays.

You learn: the scheduler, cron and interval schedules, dependent manifests, exclusions and misfire
policies.

### [10. Failures, retries and dead letters](/docs/book/failures-retries-and-dead-letters)

Sync stock from a supplier that is sometimes down. Retry it with backoff, exhaust the retries,
find it among the dead letters, and requeue it once the supplier is back.

You learn: retries, dead letters, decisions replayed on a requeue, and lifecycle hooks.

### [11. A separate worker](/docs/book/a-separate-worker)

Queue fulfilment after each order and move it to a worker process of its own. Cap it to two at a
time, kill the worker mid-run and see what happens to the run.

You learn: queueing through the mediator, trains in a class library, workers, capping queued
work, host tracking, and recovering a run whose worker died.

## Part 4: Machines

### [12. Checkout is a state machine](/docs/book/checkout-is-a-state-machine)

Rewrite checkout as a machine: cart, address, payment, placing. Each move is a typed mutation, the
server applies it, and an abandoned cart is cleared on its own. Serving a machine turns on
persisted operations, logging what they would refuse rather than refusing it.

You learn: authoring a machine, guards, timers, an authorization rule on every event, serving a
machine as its own API, and persisted operations in shadow mode.

### [13. The machine places the order](/docs/book/the-machine-places-the-order)

When checkout reaches `Placing`, the machine invokes `PlaceOrder` as the checkout's owner and moves
on with its result: placed, or failed and ready to retry without charging twice. A placed order's
parcel is a child machine the checkout waits on.

You learn: invoking a train from a state, routing its outcome back into the machine, idempotent
charges, and spawning a child machine.

## Part 5: Composition

### [14. Checks in parallel, and resuming](/docs/book/checks-in-parallel-and-resuming)

Choose fulfilment's box and ask the courier's rates side by side, then buy the label. Put a
checkpoint after the label is bought, so a fulfilment that fails later resumes there.

You learn: `Parallel` and its branches, checkpoints, and resuming a run from the console and from
GraphQL.

## Part 6: The storefront

### [15. A generated client](/docs/book/a-generated-client)

Write the storefront's GraphQL documents, export the client manifest from the host and generate a
typed React client with `trax client`. Build the catalogue page and the order page.

You learn: client documents, the client manifest, `trax client`, typed functions, following a run
from the browser, and `--check` in CI.

### [16. Checkout in the browser](/docs/book/checkout-in-the-browser)

Drive checkout from React with the generated hooks. The browser predicts each move with the
machine's twin and the server confirms it.

You learn: the TypeScript twin and the generated machine hooks.

### [17. Require persisted operations](/docs/book/require-persisted-operations)

Turn persisted operations from logging to refusing. Shopfront then runs only the documents its
storefront wrote, the ones it generates for its machines, and the console's.

You learn: requiring persisted operations, registering a frontend's documents, and what stops
working when text is refused.

## Part 7: Shipping it

### [18. Deploy](/docs/book/deploy)

Run the hub and two workers in containers against one Postgres and RabbitMQ, in Production.
Migrate the shop's schema with EF Core, add health checks, and decide what guards the console.

You learn: the [deployment topologies](/docs/reference/deployment-topologies), EF migrations
beside Trax's own, broadcasting runs between processes, health checks, and what each process
needs.

## Where to go next

- [1.1 Requirements](/docs/book/an-order-is-a-train/requirements): start the book.
- [Shopfront](https://github.com/TraxSharp/Trax/tree/main/Trax.Samples/book/Shopfront): the project
  the book ends with, every file as the last chapter leaves it, with its tests run in CI.
- [Samples](/docs/samples): complete applications that use the same pieces in other shapes.
