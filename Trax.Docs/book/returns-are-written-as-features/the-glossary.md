---
title: "14.2 The glossary"
description: "Chapter 14 of the Trax book, section 2: The glossary."
type: tutorial
area: general
parent: "14. Returns are written as features"
grand_parent: The Trax book
nav_order: 2
snippet_context: none
follows: book/returns-are-written-as-features/the-flow
---

# 14.2 The glossary

The glossary names the words the feature uses: who acts, the machine, its states, its triggers, its
guard, the train a state runs, and the view. It sits beside the feature.

`features/glossary.json`:

```json file=features/glossary.json
{
  "version": 1,
  "actors": {
    "customer": { "noun": "a customer", "roles": ["Customer"] }
  },
  "machines": {
    "order-return": {
      "ownedBy": "customer",
      "noun": "return",
      "start": "has a return",
      "initial": "Draft",
      "context": [
        { "name": "orderId", "type": "string", "nullable": false, "constraints": [] },
        { "name": "reason", "type": "string", "nullable": false, "constraints": [] }
      ],
      "states": {
        "Draft": "is a draft",
        "Checking": "is being checked",
        "Accepted": "is accepted",
        "Refused": "was refused",
        "Unchecked": "could not be checked"
      },
      "triggers": {
        "Describe": {
          "phrase": "fills in {subject} for order {orderId} because {reason}",
          "input": [
            { "name": "orderId", "type": "string", "nullable": false, "constraints": [] },
            { "name": "reason", "type": "string", "nullable": false, "constraints": [] }
          ],
          "reduce": {
            "reduce": "set",
            "steps": [
              { "field": "orderId", "value": { "input": "orderId" } },
              { "field": "reason", "value": { "input": "reason" } }
            ]
          }
        },
        "Send": { "phrase": "sends {subject}" }
      },
      "guards": {
        "hasReason": {
          "holds": "has a reason",
          "fails": "has no reason",
          "rule": { "rule": "nonEmpty", "source": "context", "field": "reason" }
        }
      },
      "invokes": {
        "Checking": {
          "train": "Shopfront.Trains.ReturnOrder.IReturnOrderTrain",
          "input": { "orderId": "orderId", "customerId": "$owner" },
          "output": [{ "name": "accepted", "type": "boolean", "nullable": false, "constraints": [] }],
          "outcomes": {
            "the return is accepted": {
              "outcome": "done",
              "when": { "rule": "boolEquals", "source": "input", "field": "accepted", "value": true }
            },
            "the return is refused": {
              "outcome": "done",
              "when": { "rule": "boolEquals", "source": "input", "field": "accepted", "value": false }
            },
            "the return cannot be checked": { "outcome": "failed" },
            "the return is called off": { "outcome": "cancelled" }
          },
          "examples": {
            "the return is accepted": { "accepted": true },
            "the return is refused": { "accepted": false }
          }
        }
      }
    }
  },
  "views": {
    "returns": {
      "reads": [{ "machine": "order-return" }],
      "fields": {
        "status": {
          "from": "order-return.state",
          "labels": {
            "Draft": "Not sent",
            "Checking": "Being checked",
            "Accepted": "Accepted",
            "Refused": "Refused",
            "Unchecked": "Not checked yet"
          }
        },
        "reason": { "from": "order-return.reason" }
      }
    }
  }
}
```

- **`actors`.** A customer is anyone whose token carries the `Customer` role, the role the hub's
  development tokens have carried since chapter 6. `noun` is how a step says it:
  `Ada is a customer`.
- **`ownedBy`, `noun`, `start`.** A return belongs to the customer who starts it. A step names one
  as `Ada's return`, and `Ada has a return` starts one.
- **`context`.** What a return holds, with the types the IR declares fields with.
- **`states`.** Each state with the words a step uses for it: `Ada's return is a draft`.
- **`triggers`.** Each with its phrase, without the person: `{subject}` is where the return is
  named, and `{orderId}` and `{reason}` are inputs, quoted in a step. `Describe`'s `reduce` writes
  both into the context. A phrase that starts with `{subject}` would be something that happens to
  the return, fired by no one.
- **`guards`.** `hasReason` holds when the reason is not empty, in the same rule vocabulary a
  served machine's guards use ([Rules](/docs/sdk-reference/statemachine-api/rules)). Its `holds`
  and `fails` phrases set a return up either way in a Given.
- **`invokes`.** `Checking` runs `IReturnOrderTrain`, found in the library by its full name. Its
  input takes the order id from the context, and `customerId` from `$owner`: the server fills in
  the return's owner, so no client can name someone else. Each outcome has a phrase. `done` splits
  on the train's output, and `examples` say what output a run in memory answers with, since nothing
  runs the train there.
- **`views`.** `returns` shows a customer their own returns: the state under a label, and the
  reason.

## SDK Reference

> [Write machines as features](/docs/statemachine/write-machines-as-features) | [Feature files](/docs/reference/feature-files) | [Feature machines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Views root](/docs/sdk-reference/graphql-api/views) | [trax CLI](/docs/reference/cli)

---

Next: [14.3 The features](/docs/book/returns-are-written-as-features/the-features)
