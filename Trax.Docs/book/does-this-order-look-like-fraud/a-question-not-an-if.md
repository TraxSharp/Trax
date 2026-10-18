---
title: "5.1 A question, not an if"
description: "Chapter 5 of the Trax book, section 1: A question, not an if."
type: tutorial
area: general
parent: "5. Does this order look like fraud"
grand_parent: The Trax book
nav_order: 1
snippet_context: none
follows: book/the-shops-own-data/test-against-a-real-database
---

# 5.1 A question, not an if

The obvious way to write this is an `if` in a junction. It works, and it hides three things: which
orders were stopped and why, which rule stopped them, and what a different rule would have done.
Trax can do better when the branch is declared on the chain as a question: the chain names every
track it can take, a **decider** answers the question on each run, and Trax records the answer.

There are three kinds of question. This one has two answers, so it is a yes or no question, routed
by a `Gate`.

| Question | Routed by | Answer |
|---|---|---|
| Which of these options? | `Switch` | One member of an enum, with a confidence |
| Where on this scale? | `Scale` | A position on an ordered enum |
| How likely is yes? | `Gate` | A probability from 0 to 1 |

The decider can be a rule table, a model or, in a test, a script. The train does not know which.

---

Next: [5.2 The question and what it is asked about](/docs/book/does-this-order-look-like-fraud/the-question-and-what-it-is-asked-about)
