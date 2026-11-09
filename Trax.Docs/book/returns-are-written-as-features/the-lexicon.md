---
title: "14.2 The lexicon"
description: "Chapter 14 of the Trax book, section 2: The lexicon."
type: tutorial
area: general
parent: "14. Returns are written as features"
grand_parent: The Trax book
nav_order: 2
snippet_context: none
follows: book/returns-are-written-as-features/the-flow
redirect_from:
  - /docs/book/returns-are-written-as-features/the-glossary
---

# 14.2 The lexicon

The lexicon names the words the feature uses: who acts, the machine, its states, its triggers, its
guard, the train a state runs, and the view. It sits beside the feature, one line per fact, each
line starting with a keyword. Indentation is for reading only.

`features/returns.lexicon`:

```text file=features/returns.lexicon
# What the returns features' words mean. `dotnet trax machine from-gherkin` elaborates it to
# machines/from-features/glossary.json, with the machine it derives.
Version: 1

Actor: customer "a customer"
  Roles: Customer

Machine: order-return "return"
  Owned by: customer
  Started by: "has a return"

  State: Draft      "is a draft"
  State: Checking   "is being checked"
  State: Accepted   "is accepted"
  State: Refused    "was refused"
  State: Unchecked  "could not be checked"

  Trigger: Describe "fills in {subject} for order {orderId} because {reason}"
    Input: orderId is a string
    Input: reason is a string
    Sets: orderId, reason
  Trigger: Send     "sends {subject}"

  Guard: hasReason when reason is not empty
    Holds: "has a reason"
    Fails: "has no reason"

  Runs: Shopfront.Trains.ReturnOrder.IReturnOrderTrain in Checking
    Takes: orderId
    Input: customerId from the owner
    Returns: accepted is a boolean
    Outcome: "the return is accepted"        when accepted is true
    Outcome: "the return is refused"         when accepted is false
    Outcome: "the return cannot be checked"  when it fails
    Outcome: "the return is called off"      when it is called off

View: "returns"
  Reads: order-return
  Field: status from order-return's state
    Label: Draft      "Not sent"
    Label: Checking   "Being checked"
    Label: Accepted   "Accepted"
    Label: Refused    "Refused"
    Label: Unchecked  "Not checked yet"
  Field: reason from order-return's reason
```

- **`Actor:`.** A customer is anyone whose token carries the `Customer` role, the role the hub's
  development tokens have carried since chapter 6. The quoted words are how a step says it:
  `Ada is a customer`.
- **`Machine:`, `Owned by:`, `Started by:`.** The machine's id is `order-return`, and `"return"` is
  how a step names one: `Ada's return`. A return belongs to the customer who starts it, and
  `Ada has a return` starts one.
- **`State:`.** Each state with the words a step uses for it: `Ada's return is a draft`. The first
  is where a return starts.
- **`Trigger:`.** Each with its phrase, without the person: `{subject}` is where the return is
  named, and `{orderId}` and `{reason}` are inputs, quoted in a step. `Input:` gives each its type,
  and `Sets:` writes both into the context. A phrase that starts with `{subject}` would be
  something that happens to the return, fired by no one.
- **`Guard:`.** `hasReason` holds when the reason is not empty, in the same rule vocabulary a
  served machine's guards use ([Rules](/docs/sdk-reference/statemachine-api/rules)). Its `Holds:`
  and `Fails:` phrases set a return up either way in a Given.
- **`Runs:`.** `Checking` runs `IReturnOrderTrain`, found in the library by its full name.
  `Takes:` gives it the order id from the context, and `Input: customerId from the owner` gives it
  the return's owner: the server fills that in, so no client can name someone else. `Returns:` is
  what the train answers with. Each `Outcome:` has a phrase; the two `when accepted is ...` lines
  split a finished run on that answer, and the other two are a run that failed or was called off.
- **`View:`.** `returns` shows a customer their own returns: the state under a label, and the
  reason.

There is no `Context:` line. The context is what the lines write: `Sets:` writes `orderId` and
`reason` from the trigger's input, so a return holds both, as strings. The rest is filled in the
same way, from rules the [lexicon reference](/docs/reference/lexicon#what-is-inferred) lists.

The lexicon is what you write. `trax machine from-gherkin` elaborates it to `glossary.json`, the
same words in the JSON every engine reads, and writes that beside the machine in 14.4.

## SDK Reference

> [Write machines as features](/docs/statemachine/write-machines-as-features) | [Lexicon](/docs/reference/lexicon) | [Feature files](/docs/reference/feature-files) | [Feature machines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Views root](/docs/sdk-reference/graphql-api/views) | [trax CLI](/docs/reference/cli)

---

Next: [14.3 The features](/docs/book/returns-are-written-as-features/the-features)
