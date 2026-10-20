---
title: "6. Customers and staff"
description: "Chapter 6 of the Trax book: sign customers and staff in, guard PlaceOrder with TraxAuthorize, and scope each order to the customer who placed it."
type: tutorial
area: general
parent: The Trax book
nav_order: 16
redirect_from:
  - /docs/samples/auth
---

# 6. Customers and staff

Until now anyone could call `POST /orders`, and say they were anyone: the customer was a field in
the request. In this chapter Shopfront learns who is asking. A customer signs in and places orders
as themselves, reads their own orders and nobody else's, and a member of staff reads every order
but cannot place one.

This chapter starts from the end of [chapter 5](/docs/book/does-this-order-look-like-fraud).

## In this chapter

1. [Fail closed](/docs/book/customers-and-staff/fail-closed)
2. [Packages](/docs/book/customers-and-staff/packages)
3. [Roles and a development signing key](/docs/book/customers-and-staff/roles-and-a-development-signing-key)
4. [Who may run PlaceOrder](/docs/book/customers-and-staff/who-may-run-placeorder)
5. [A customer reads their own orders](/docs/book/customers-and-staff/a-customer-reads-their-own-orders)
6. [Sign in, and run trains as the caller](/docs/book/customers-and-staff/sign-in-and-run-trains-as-the-caller)
7. [Try it](/docs/book/customers-and-staff/try-it)
8. [What it refuses](/docs/book/customers-and-staff/what-it-refuses)
9. [Test it](/docs/book/customers-and-staff/test-it)

## SDK Reference

> [AddTraxJwtAuth](/docs/sdk-reference/api-auth/add-trax-jwt-auth) | [TraxAuthorize](/docs/sdk-reference/attributes/trax-authorize) | [TraxPrincipal](/docs/security/inject-the-current-principal) | [ITrainExecutionService](/docs/sdk-reference/mediator-api/i-train-execution-service) | [Scope a data context to the caller](/docs/effect/scope-a-data-context-to-the-caller) | [Effect data testing](/docs/sdk-reference/testing/effect-data-testing)

---

Next: [6.1 Fail closed](/docs/book/customers-and-staff/fail-closed)
