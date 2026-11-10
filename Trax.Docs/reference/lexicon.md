---
title: Lexicon
description: "Reference for the lexicon, the file that says what a features directory's words mean: every keyword, its block, its one form and what is inferred."
type: reference
area: machines
nav_order: 8
source:
  - Trax.StateMachine/dotnet/src/Trax.StateMachine.Gherkin/Lexicon.cs
  - Trax.StateMachine/conformance/lexicon.md
---

# Lexicon

The lexicon is the file you write beside your features to say what their words mean: who acts, the machines, their
states, triggers, guards and trains, and the views. It is line-based, in the style of Gherkin, one file per features
directory, named `<anything>.lexicon`, and it may hold any number of machines. `trax machine from-gherkin` elaborates
it to the glossary, filling in everything that follows a rule, and writes that `glossary.json` beside the IRs, where
it is committed and checked like the other derived files. The glossary's keys are in the
[feature files reference](/docs/reference/feature-files#the-glossary). Why the lexicon is the source and the glossary
is derived is
[ADR 0059](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0059-the-lexicon-is-the-source-and-the-glossary-is-derived.md),
and the grammar the reader is held to is
[`conformance/lexicon.md`](https://github.com/TraxSharp/Trax/blob/main/Trax.StateMachine/conformance/lexicon.md).

A lexicon is read by the .NET tooling only. Every engine reads the glossary.

```text
Version: 1

Actor: customer "a customer"
  Roles: customer

Machine: order
  Owned by: customer
  Started by: "has an order"
  Context: approved is a boolean, may be empty

  State: Draft     "is a draft"
  State: Paying    "is being paid for"
  State: Placed    "is placed"
  State: Declined  "was declined"

  Trigger: Order "orders {loaves} loaves for {subject}"
    Input: loaves is an integer
    Sets: loaves
  Trigger: Place "places {subject}"

  Guard: hasLoaves when loaves is more than 0
    Holds: "has something in it"
    Fails: "has nothing in it"

  Runs: Bakery.Trains.ITakePayment in Paying
    Input: amount from loaves
    Returns: approved is a boolean
    Outcome: "the payment goes through"     when approved is true
    Outcome: "the payment is declined"      when approved is false
    Outcome: "the payment cannot be taken"  when it fails

View: "orders"
  Reads: order
  Field: status from order's state
    Label: Draft  "Not sent"
```

## Lines

- Every line starts with a keyword from the tables below. A line that does not is a problem, and the nearest keyword
  is suggested (`Outcom:` gives `Outcome:`). There is no free text: a note goes after `#`.
- Whitespace outside quotes never matters. Indentation and lined-up columns are for reading;
  [`trax lexicon format`](/docs/reference/cli#trax-lexicon-format) lays a lexicon out one way.
- Phrases, labels, names of views and text values are quoted. Inside quotes, `\"` is a quote, `\\` a backslash and
  `#` is text. Any other backslash is a problem.
- A keyword belongs under one kind of block and attaches to the nearest one still open, closing every block opened
  after it. A machine's `Context:` lines therefore come before its first `State:`: after one, `Context:` belongs to
  that state.
- Each keyword has one form, given in the tables. A line not in that form is a problem that names the form.
- A name, phrase or outcome is defined once. A second definition is a problem naming the first one's line.
- A lexicon with any problem gives no glossary. Every problem names its lexicon line, including the ones the glossary
  reader and derivation find later.
- A second lexicon in the same directory is a problem, and so is a `glossary.json` beside a lexicon.
- `Version: 1` is the first line; only notes and blank lines come before it. A keyword's colon may be written against
  the word after it (`Version:1`), and a lone surrogate anywhere on a line is a problem.

## Top level

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Version:` | `Version: 1`, the first line | `version` |
| `Trains in:` | `Trains in: Bakery.Trains` | the namespace of every train a `Runs:` line names without a dot |
| `Actor:` | `Actor: customer "a customer"` | `actors.customer.noun` |
| `Constant:` | `Constant: EmptyBoard is "...."` | nothing: a value used by name in `Sets:`, `Example:` and `Starts with:` |
| `Type:` | `Type: Square` | nothing: a type a field names (`is a Square`) |
| `Machine:` | `Machine: answer-sheet`, or `Machine: shop "the shop"` | `machines.<id>`; its noun, or a system machine's name, is the id with hyphens as spaces unless quoted |
| `View:` | `View: "the shop sign"` | `views.<name>` |

## Under `Actor:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Roles:` | `Roles: Clerk, Manager` | `roles` |
| `Any signed-in` | alone on its line | `anyAuthenticated: true` |

An actor has one of the two, and is refused with both.

## Under `Type:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Values:` | `Values: "S", "M", "L"`, or a range: `Values: A to J, each followed by 1 to 10` | a string field with that `enum` (`A1` to `J10`) |
| `Has:` | `Has: postcode is a string, may be empty`, one per field | an object field with those `fields` |

A type is one or the other. A range is two letters from `A` to `Z` (or `a` to `z`) or two whole numbers, and a
`Values:` line gives at most 1000 values. A record that contains itself, directly or through another type, is a
problem at its `Has:` line.

## Under `Machine:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Owned by:` | `Owned by: member, clerk` | `ownedBy`, a string for one actor and a list for several |
| `System` | alone on its line | `system: "<id>/main"` |
| `System at:` | `System at: flight/tx101` | `system: "flight/tx101"` |
| `System keyed by:` | `System keyed by: shopId, tillId` | `system: { "keyedBy": [...] }` |
| `Started by:` | `Started by: "has an order"` | `start` |
| `Begins in:` | `Begins in: Draft` | `initial`; without it, the first state |
| `Version:` | `Version: 2` | `version` |
| `Migrates from:` | `Migrates from: 1 with Shop.Migrations.CheckoutFromVersion1` | `migrations."1"` |
| `Differential` | alone on its line | `differential: true` |
| `Starts with:` | `Starts with: items is [], receipt is nothing` | `initialContext`, each value typed by its field |
| `Context:` | a field (see [Fields](#fields)), or `Context: target may be empty` | `context` (see [What is inferred](#what-is-inferred)) |
| `State:` | `State: Paid "is paid"`, or `State: Paid "is paid" committed` | `states.Paid` |
| `Trigger:` | `Trigger: Fire "fires at {square} in {subject}"` | `triggers.Fire` |
| `Guard:` | `Guard: hasLoaves when loaves is more than 0` | `guards.hasLoaves.rule` |
| `Runs:` | `Runs: FireShot in Firing`, or `Runs: Bakery.Trains.ITakePayment in Paying` | `invokes.Firing`, train `<Trains in>.IFireShotTrain` or the dotted name as written |
| `Link:` | `Link: "the receipt for {subject}" to order by orderId` | `links` |
| `Rule:` | `Rule: checkout:priced with Shop.Rules.TotalIsPriced, example true` | `rules` |
| `Reduction:` | `Reduction: checkout:add-item with Shop.Rules.AddItem, example items is ["bread"]` | `reductions` |

A machine has `Owned by:` or one of the three `System` lines, and is refused with both or neither.

## Under `State:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Requires:` | `Requires: receipt is not empty`, a rule (see [Rules](#rules)) | `requires` |
| `Context:` | a field, one per line; no `may be empty` form | the state's own `context`, in place of the machine's |

## Under `Trigger:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Input:` | a field, one per line: `Input: square is a Square` | `input` |
| `Sets:` | `Sets: target to square, loaves, shots to EmptyBoard` | `reduce: set`; a bare field is set from the input of its name |
| `Keeps` | alone on its line | `reduce: keep` |
| `Clears` | alone on its line | `reduce: clear` |
| `Resets` | alone on its line | `reduce: reset` |
| `Reduces with:` | `Reduces with: checkout:add-item` | `reduce: custom` |
| `Guarded by:` | `Guarded by: hasLoaves` | `guard` |
| `Effect:` | `Effect: Shop.Effects.ICharge, key prefix "checkout:charge", example "rcpt-1"`; the key prefix may be left out | `effect` |

A trigger reduces once: one of `Sets:`, `Keeps`, `Clears`, `Resets` and `Reduces with:`.

## Under `Guard:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Holds:` | `Holds: "has something in it"` | `holds` |
| `Fails:` | `Fails: "has nothing in it"` | `fails` |

A guard that reads only the trigger's input may leave both out.

## Under `Runs:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Takes:` | `Takes: fleet, shots, owner` | `input`, each from the context field of its name; `owner` from `$owner` |
| `Input:` | `Input: square from target`, or `Input: memberId from the owner` | `input.square: "target"`, `input.memberId: "$owner"` |
| `Returns:` | `Returns: result is a string, shots is a string` | `output`, sorted by name |
| `Stores:` | `Stores: shots, result as yourShot` | a set step on every done outcome: `yourShot` from the output `result` |
| `Sets:` | `Sets: shots to EmptyBoard` | a set step on every done outcome, from an output field, a constant or a value |
| `Example:` | `Example: fleet is "f-1", board is EmptyBoard` | each done outcome's `examples` entry, where its `when` does not give the value |
| `Outcome:` | `Outcome: "the shot misses" when result is "miss"`, or `when it is done`, `when it fails`, `when it is called off` | `outcomes`: `done` with its `when`, `done`, `failed`, `cancelled` |

Given the train assemblies (`trax machine from-gherkin --trains`, or `Lexicon.Read` with assemblies), `Takes:` and
`Returns:` are optional and read from the train's records. A `Returns:` field that differs from the record's in type,
format, nullability or values is a problem at its line; one that agrees is what the glossary holds, so it is the same
with the assemblies and without. A train whose types do not load is a problem at its `Runs:` line.

## Under `View:` and `Field:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Reads:` | `Reads: order, shop` | `reads`; a system machine at a fixed key is read at it |
| `Field:` | `Field: status from game's state`, `Field: shot from game's yourShot` | `fields.status.from: "game.state"` |
| `Label:` (under `Field:`) | `Label: Dealing "Dealing the fleets"` | `labels.Dealing` |

## Fields

A field is `<name> is a <type>`, then clauses after commas:

| Clause | Glossary |
| --- | --- |
| `may be empty` | `nullable: true`; without it a field is never null |
| `at least 1`, `at most 12` | `minimum`, `maximum` |
| `one of "S", "M", "L"` | `enum` |
| `where <rule>`, last | `constraints` |

| Type | Written |
| --- | --- |
| Plain | `a string`, `a number`, `an integer`, `a boolean`, `an object`, `a list` |
| String formats | `a uuid`, `a date`, `a time`, `a date-time`, `a date-time-offset`, `a duration`, `a uri`, `a char` |
| Named | a `Type:` by name: `a Square`, `an Address` |
| List | `a list of <type>`: `a list of strings`, `a list of Squares` |
| Map | `a map of <type> to <type>`: `a map of string to integer`. Keys that are any string write no `keys` shape, as the host writes a `Dictionary<string, T>`. |

Nothing nests inside a line: a record in a list is a named type. `Returns:` holds several fields on one line,
separated by commas; a comma followed by a clause goes on with the field before it.

## Rules

A rule is one comparison, or several joined by `and` or by `or`. A line that mixes the two is a problem: a rule that
needs both is a custom rule, written in C#, named in the lexicon (`checkout:priced holds`) and bound by a `Rule:` line.

| Comparison | IR rule |
| --- | --- |
| `receipt is present`, `receipt is absent` | `present`, `absent` |
| `receipt is not empty` | `nonEmpty` |
| `receipt is a string` | `ofType` (string, number, integer, boolean, list, object) |
| `tags is a list of strings` | `arrayOf` |
| `size is one of "S", "M"` | `oneOf` |
| `result is "hit"` | `oneOf` with the one value: the IR has no string equality |
| `approved is true` | `boolEquals` |
| `loaves is 3`, `is at least 3`, `is at most 3`, `is more than 3`, `is less than 3` | `compare`: `eq`, `gte`, `lte`, `gt`, `lt` |
| `tags has 3 items`, `has at least 3 items`, `has fewer than 3 items` | `count` |
| `code is 3 characters long`, `is at least 3 characters long` | `length` |
| `checkout:priced holds` | `custom` |

A field is read from the context in `Requires:`, `Guard:` and `where`, and from what the train returns in an
outcome's `when`. `input item` and `context item` say which where it is not the default: a guard on the trigger's
input reads `input item is not empty`. The IR rules are in
[the IR format](/docs/sdk-reference/statemachine-api/ir-format#transitions).

## Values

A value is typed by the field it is for. A string is quoted, a number and an integer bare (an integer whole), a
boolean `true` or `false`, a list in brackets (`["bread", "rye"]`), and `nothing` is null, allowed only where the
field may be empty. A bare name is a constant, or, in `Sets:` and `Stores:`, a field of what the line reads from. A
value of the wrong type is a problem at its line, and so is a value in another case than the field's declared one
(`"m"` for `"S", "M", "L"`), since rules compare values as written. A list nests at most 32 deep, and a constant that
comes back round to itself is a problem at its line. An object field takes no value in a lexicon; set it with a custom
reduction.

## What is inferred

| What | How |
| --- | --- |
| A machine's noun | Its id with hyphens as spaces, unless quoted after the id. A system machine's name likewise, and a system machine at a fixed key is at `<id>/main` unless `System at:` says otherwise. |
| The context | Each field a `Context:` line declares, in the order written, then each field something writes and no line declares, sorted by name, with the type of what writes it: a trigger's input, a train's output, a constant or a value. A written field is never null unless `Context: <field> may be empty` says so. Two writes that disagree on the type are a problem at the second. |
| A train's full name | A dotted name as written; a plain one is `I<Name>Train` in the namespace of `Trains in:`, which is then required. |
| A train's input | Each `Takes:` field from the context field of its name, `owner` from the instance owner, and each `Input:` line as written. Given the train assemblies, every input the record has and no line writes is wired by the same rule. |
| A train's output | From `Returns:`, sorted by name; given the train assemblies, from the output record as the host reads it. |
| A done outcome | Stores what `Stores:` and `Sets:` say. Its example is the value its `when` pins for each output field, then the `Example:` line's. An output field with neither is a problem at the outcome. |
| A view's reads | A read of a system machine at a fixed key carries the key. |

The generated `glossary.json` is committed, so a reviewer can see what was filled in. It is written in one canonical
form (keys in schema order, maps in declared order, two-space indent, a trailing newline), so `from-gherkin --check`
compares it byte for byte.

## SDK Reference

> [Lexicon](/docs/sdk-reference/statemachine-api/feature-machines#lexicon) | [Feature files](/docs/reference/feature-files) | [Write machines as features](/docs/statemachine/write-machines-as-features) | [trax CLI](/docs/reference/cli#machines-from-features)
