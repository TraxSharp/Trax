---
title: "8.1 The console reads through GraphQL"
description: "Chapter 8 of the Trax book, section 1: The console reads through GraphQL."
type: tutorial
area: general
parent: "8. The operations console"
grand_parent: The Trax book
nav_order: 1
snippet_context: none
follows: book/serve-it-over-graphql/test-it
---

# 8.1 The console reads through GraphQL

The console is a single-page app Trax ships. It holds no data of its own: everything it shows or
changes is a GraphQL request to the `operations` namespace of Shopfront's schema, which is off
until a host turns it on and refuses to start turned on without a gate. That gate is what keeps
customers out, on every request, whatever page they load.

---

Next: [8.2 Packages](/docs/book/the-operations-console/packages)
