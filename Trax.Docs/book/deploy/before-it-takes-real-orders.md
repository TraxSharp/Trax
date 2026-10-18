---
title: "18.10 Before it takes real orders"
description: "Chapter 18 of the Trax book, section 10: Before it takes real orders."
type: tutorial
area: general
parent: "18. Deploy"
grand_parent: The Trax book
nav_order: 10
snippet_context: none
follows: book/deploy/the-console
---

# 18.10 Before it takes real orders

- **HTTPS** in front of the hub, terminated at the ingress or the load balancer.
- **Secrets** from a secret store, not a file in the repository.
- **Fakes.** The payment provider, the courier, the supplier and the warehouse are fakes, registered
  in `AddShopfront`. Each is an interface, so a real one replaces it there, and nothing else
  changes.
- **A second hub** needs persisted-operation changes broadcast between hubs: replace `SingleNode()`
  with `UseRabbitMqInvalidation(rabbitMq)`. Only one hub runs the schedule at a time; the others
  serve.

## What we have

Shopfront, deployed: a hub that serves the storefront and the console, workers that run the jobs,
and the two sharing one Postgres and one RabbitMQ. That is the end of the book.

---

That is the end of the book. [Back to the contents](/docs/book).
