---
title: "17.4 What stops working"
description: "Chapter 17 of the Trax book, section 4: What stops working."
type: tutorial
area: general
parent: "17. Require persisted operations"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/require-persisted-operations/what-a-caller-sees
---

# 17.4 What stops working

- **`curl` with a document written as text**, which every earlier chapter used. In Development,
  start the hub with `PersistedOperations__Require=false` to explore by hand again.
- **The GraphQL IDE** at `/trax/graphql` sends text and introspects. It is served in Development
  only, and refused with the rest there now.
- **A client this hub did not generate.** A mobile app, a partner's integration or a script that
  writes its own documents has to have them registered: as documents of its own, through
  `trax client --documents` like the storefront's, or uploaded through the persisted-operations
  management fields.

---

Next: [17.5 Test it](/docs/book/require-persisted-operations/test-it)
