---
title: "11. A separate worker"
description: "Chapter 11 of the Trax book: queue fulfilment after each order, move the trains into a library, and run queued and scheduled work on a worker process."
type: tutorial
area: general
parent: The Trax book
nav_order: 21
---

# 11. A separate worker

Once an order is paid, someone has to pack it. Packing takes a while, and the customer should not
wait for it, so `PlaceOrder` queues it and answers. In this chapter fulfilment becomes queued work,
and every piece of work nobody waits for, the queued packing and the scheduled jobs from the last
two chapters, moves to a process of its own: a worker. The process customers talk to only takes
requests and schedules.

Then we kill the worker in the middle of packing an order, and see what Trax does about it.

This chapter starts from the end of [chapter 10](/docs/book/failures-retries-and-dead-letters).

## In this chapter

1. [One library, two processes](/docs/book/a-separate-worker/one-library-two-processes)
2. [The projects](/docs/book/a-separate-worker/the-projects)
3. [Fulfilment is a train](/docs/book/a-separate-worker/fulfilment-is-a-train)
4. [PlaceOrder queues it](/docs/book/a-separate-worker/placeorder-queues-it)
5. [The hub](/docs/book/a-separate-worker/the-hub)
6. [The worker](/docs/book/a-separate-worker/the-worker)
7. [Run both](/docs/book/a-separate-worker/run-both)
8. [Kill the worker mid-run](/docs/book/a-separate-worker/kill-the-worker-mid-run)
9. [Test it](/docs/book/a-separate-worker/test-it)

## SDK Reference

> [AddTraxWorker](/docs/sdk-reference/scheduler-api/add-trax-worker) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Queued work](/docs/mediator/queued-work) | [Job submission](/docs/scheduler/job-submission) | [Structuring your application](/docs/reference/project-layout) | [Host tracking](/docs/effect/host-tracking)
