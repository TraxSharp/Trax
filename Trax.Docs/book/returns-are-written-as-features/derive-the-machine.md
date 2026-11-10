---
title: "14.4 Derive the machine"
description: "Chapter 14 of the Trax book, section 4: Derive the machine."
type: tutorial
area: general
parent: "14. Returns are written as features"
grand_parent: The Trax book
nav_order: 4
snippet_context: none
follows: book/returns-are-written-as-features/the-features
---

# 14.4 Derive the machine

`trax machine from-gherkin` reads the feature and the lexicon and writes the machine. It came with
the tool installed in chapter 12. From the solution folder:

```bash
dotnet trax machine from-gherkin --features features --out machines/from-features
```

```text
wrote access.txt
wrote glossary.json
wrote host.json
wrote order-return.ir.json
```

The tool owns the folder it writes to and removes a machine that no longer derives, so it gets one
of its own beside checkout's `machines/checkout`. Before it writes anything it replays every
scenario on what it derived and fails on one that does not pass. Two scenarios that disagree, a
Given or When it cannot read, or a feature that would derive a machine the hub refuses, are
reported with the file and the line, and nothing is written.

`glossary.json` is the lexicon elaborated: every word it gave, and what it left to a rule written
out, such as the context fields `Sets:` implied and each outcome's example. It is derived like the
rest of the folder, never edited.

`machines/from-features/glossary.json`:

```json file=machines/from-features/glossary.json
{
  "version": 1,
  "actors": {
    "customer": {
      "noun": "a customer",
      "roles": [
        "Customer"
      ]
    }
  },
  "machines": {
    "order-return": {
      "ownedBy": "customer",
      "noun": "return",
      "start": "has a return",
      "initial": "Draft",
      "context": [
        {
          "name": "orderId",
          "type": "string",
          "nullable": false,
          "constraints": []
        },
        {
          "name": "reason",
          "type": "string",
          "nullable": false,
          "constraints": []
        }
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
            {
              "name": "orderId",
              "type": "string",
              "nullable": false,
              "constraints": []
            },
            {
              "name": "reason",
              "type": "string",
              "nullable": false,
              "constraints": []
            }
          ],
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "orderId",
                "value": {
                  "input": "orderId"
                }
              },
              {
                "field": "reason",
                "value": {
                  "input": "reason"
                }
              }
            ]
          }
        },
        "Send": {
          "phrase": "sends {subject}"
        }
      },
      "guards": {
        "hasReason": {
          "holds": "has a reason",
          "fails": "has no reason",
          "rule": {
            "rule": "nonEmpty",
            "source": "context",
            "field": "reason"
          }
        }
      },
      "invokes": {
        "Checking": {
          "train": "Shopfront.Trains.ReturnOrder.IReturnOrderTrain",
          "input": {
            "orderId": "orderId",
            "customerId": "$owner"
          },
          "output": [
            {
              "name": "accepted",
              "type": "boolean",
              "nullable": false,
              "constraints": []
            }
          ],
          "outcomes": {
            "the return is accepted": {
              "outcome": "done",
              "when": {
                "rule": "boolEquals",
                "source": "input",
                "field": "accepted",
                "value": true
              }
            },
            "the return is refused": {
              "outcome": "done",
              "when": {
                "rule": "boolEquals",
                "source": "input",
                "field": "accepted",
                "value": false
              }
            },
            "the return cannot be checked": {
              "outcome": "failed"
            },
            "the return is called off": {
              "outcome": "cancelled"
            }
          },
          "examples": {
            "the return is accepted": {
              "accepted": true
            },
            "the return is refused": {
              "accepted": false
            }
          }
        }
      }
    }
  },
  "views": {
    "returns": {
      "reads": [
        {
          "machine": "order-return"
        }
      ],
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
        "reason": {
          "from": "order-return.reason"
        }
      }
    }
  }
}
```

Commit the folder. `access.txt` is the part to read in review:

```text
@customer: roles Customer
order-return Describe: customer
order-return Send: customer
order-return read: customer
order-return start: customer
view returns: customer
```

- **`@customer: roles Customer`** is who counts as a customer.
- **`start`** is there because `Ada has a return`, **`Describe` and `Send`** because Ada fills in
  and sends her own return, and **`read`** because a customer reads the returns they own.
- **`view returns`** is there because `Ada's returns show ...`.

Each line is a grant a scenario showed. A grant added to a feature is a new line in this file in
the same pull request, and a word changed in the lexicon is a change to `glossary.json`. `dotnet trax machine from-gherkin --features features --out
machines/from-features --check` writes nothing and fails when a committed file is stale, and is the
step to run in CI.

When a scenario does not derive what was meant, `explain` prints what derivation read from it:

```bash
dotnet trax machine explain --features features --scenario "no reason"
```

```text
returns.feature:20 A return with no reason is refused
  grant: order-return Send is served to Customer
  guard: order-return Send from Draft moves when its guard holds and is refused when it fails
  start: order-return is started by Customer
```

## SDK Reference

> [Write machines as features](/docs/statemachine/write-machines-as-features) | [Feature files](/docs/reference/feature-files) | [Feature machines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Views root](/docs/sdk-reference/graphql-api/views) | [trax CLI](/docs/reference/cli)

---

Next: [14.5 The check](/docs/book/returns-are-written-as-features/the-check)
