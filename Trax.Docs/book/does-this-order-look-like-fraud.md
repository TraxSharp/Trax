---
title: "5. Does this order look like fraud"
description: "Chapter 5 of the Trax book: stop an order that looks like fraud with a Gate a rule decider answers, record each decision, and try a stricter rule in shadow."
type: tutorial
area: general
parent: The Trax book
nav_order: 15
---

# 5. Does this order look like fraud

A stolen card is usually used fast and for a lot, often on an account that has never ordered
before. In this chapter `PlaceOrder` asks whether an order looks like fraud before it reserves any
stock or charges anyone, and refuses the ones that do. The answer comes from a rule today, and the
train is written so that a model can give it tomorrow without the train changing.

By the end, every answer is recorded with the run that asked, and a stricter rule is asked beside
the live one on every order, so we can see where the two would disagree before trusting the new
one.

This chapter starts from the end of [chapter 4](/docs/book/the-shops-own-data).

## In this chapter

1. [A question, not an if](/docs/book/does-this-order-look-like-fraud/a-question-not-an-if)
2. [The question and what it is asked about](/docs/book/does-this-order-look-like-fraud/the-question-and-what-it-is-asked-about)
3. [The tracks](/docs/book/does-this-order-look-like-fraud/the-tracks)
4. [Register Shopfront in one place](/docs/book/does-this-order-look-like-fraud/register-shopfront-in-one-place)
5. [Run it](/docs/book/does-this-order-look-like-fraud/run-it)
6. [Read the decisions](/docs/book/does-this-order-look-like-fraud/read-the-decisions)
7. [Test it](/docs/book/does-this-order-look-like-fraud/test-it)

## SDK Reference

> [Gate](/docs/sdk-reference/train-methods/gate) | [Decide](/docs/sdk-reference/train-methods/decide) | [AddDecisionRecording](/docs/sdk-reference/configuration/add-decision-recording) | [Write a decider](/docs/core/write-a-decider) | [Try a decider in shadow](/docs/core/try-a-decider-in-shadow) | [Test a train's decisions](/docs/core/test-train-decisions)

---

Next: [5.1 A question, not an if](/docs/book/does-this-order-look-like-fraud/a-question-not-an-if)
