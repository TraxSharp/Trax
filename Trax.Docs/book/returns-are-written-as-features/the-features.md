---
title: "14.3 The features"
description: "Chapter 14 of the Trax book, section 3: The features."
type: tutorial
area: general
parent: "14. Returns are written as features"
grand_parent: The Trax book
nav_order: 3
snippet_context: none
follows: book/returns-are-written-as-features/the-lexicon
---

# 14.3 The features

The scenarios say what returns do, in the shop's words. Derivation reads them, and so do the tests.

`features/returns.feature`:

```gherkin file=features/returns.feature
Feature: Returning an order
  A customer sends an order back and says why. The shop checks the order is theirs before it takes
  the return.

  Background:
    Given Ada is a customer
    And Ada has a return

  Scenario: A return says which order and why
    When Ada fills in her return for order "3f2b8c1e-5d4a-4f7b-9c2e-1a6d8e0b7f45" because "It arrived broken"
    Then Ada's return is a draft
    And Ada's returns show reason "It arrived broken"

  Scenario: A return with a reason is sent to be checked
    Given Ada's return has a reason
    When Ada sends her return
    Then Ada's return is being checked
    And Ada's returns show status "Being checked"

  Scenario: A return with no reason is refused
    Given Ada's return has no reason
    When Ada sends her return
    Then Ada is told "Say why you are sending it back."
    And Ada's return is still a draft

  # The tests start the hub alone, and the hub hands every job to the worker: a scenario that
  # waits for the check's answer runs in memory only.
  @in-memory
  Scenario: The shop accepts the return of Ada's own order
    Given Ada's return has a reason
    When Ada sends her return
    Then Ada's return is being checked
    When the return is accepted
    Then Ada's return is accepted
    And Ada's returns show status "Accepted"

  @in-memory
  Scenario: The shop refuses the return of an order that is not Ada's
    Given Ada's return has a reason
    When Ada sends her return
    Then Ada's return is being checked
    When the return is refused
    Then Ada's return was refused
    And Ada's returns show status "Refused"

  @in-memory
  Scenario: A check that fails leaves the return unchecked
    Given Ada's return has a reason
    When Ada sends her return
    Then Ada's return is being checked
    When the return cannot be checked
    Then Ada's return could not be checked

  @in-memory
  Scenario: A check called off leaves the return unchecked
    Given Ada's return has a reason
    When Ada sends her return
    Then Ada's return is being checked
    When the return is called off
    Then Ada's return could not be checked
    And Ada's returns show status "Not checked yet"
```

Each scenario names its people and speaks of them in the third person: `Ada is a customer`, then
`Ada's return`, and `her return` for the step's own person.

- **A Given sets a return up**, without acting: `Ada's return has a reason` writes a reason into
  the context.
- **A When acts.** `When Ada sends her return` is Ada firing `Send` on her own return. That is
  also what grants customers `Send`: a trigger no scenario shows anyone firing is not served at
  all. `When the return is accepted` is the train's outcome, which no person fires.
- **A Then says where it went.** `Ada is told "Say why you are sending it back."` says the move was
  refused by a guard, and that sentence becomes the guard's message. `Ada's returns show status
  "Accepted"` reads the view, which makes customers its readers.
- **`@in-memory`** is Shopfront's own tag, for the scenarios that wait on the train. Section 14.9
  says why they run in memory only.

A step quotes every value. `"It arrived broken"` is the reason; the same words unquoted would be
part of the phrase, and match nothing.

## SDK Reference

> [Write machines as features](/docs/statemachine/write-machines-as-features) | [Feature files](/docs/reference/feature-files) | [Feature machines](/docs/sdk-reference/statemachine-api/feature-machines) | [Feature bindings](/docs/sdk-reference/statemachine-api/feature-bindings) | [AddStateMachines](/docs/sdk-reference/statemachine-api/add-state-machines) | [Views root](/docs/sdk-reference/graphql-api/views) | [trax CLI](/docs/reference/cli)

---

Next: [14.4 Derive the machine](/docs/book/returns-are-written-as-features/derive-the-machine)
