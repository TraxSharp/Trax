# The lexicon

The lexicon is the file people write to say what their features' words mean. Trax elaborates it to the glossary
(`glossary.schema.json`, version 1), filling in everything that follows a rule, and writes that glossary beside the
IRs, where it is committed and checked like any other derived file (`Trax.Docs/adr/0059`). This page is the grammar.
The reader is `Lexicon.Read` in `Trax.StateMachine.Gherkin`; `formats/lexicon/` holds the cases it is held to.

The lexicon is a .NET-only format. It has no JSON Schema and no TypeScript reader: every engine reads the glossary,
and only the .NET tooling writes it.

## Lines

- A lexicon is one file per features directory, named `<anything>.lexicon`. It may hold any number of machines; a
  second lexicon in the directory is a problem.
- Every line starts with a keyword from the closed set below. A line that does not is a problem, with the nearest
  keyword suggested (`Outcom:` → `Outcome:`). There is no free text: a note goes after `#`.
- Whitespace outside quotes never matters. Indentation, tabs and lined-up columns are for reading only.
- Phrases, labels, names of views and text values are quoted. Inside quotes, `\"` is a quote, `\\` a backslash and
  `#` is text; any other backslash is a problem.
- A keyword belongs under one kind of block, and attaches to the nearest one still open, closing every block opened
  after it. So a machine's `Context:` lines come before its first `State:`: after one, `Context:` belongs to that
  state.
- Each keyword has one form, given in the tables. A line not in it is a problem naming the form.
- A name, phrase or outcome is defined once; a second definition names the first one's line.
- Nothing is skipped and nothing is read in part. A lexicon with any problem gives no glossary, and every problem
  names its line, including the ones the glossary reader and derivation find later: they are mapped back to the line
  that wrote the key.

## Keywords

### Top level

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Version:` | `Version: 1`, first | `version` |
| `Trains in:` | `Trains in: Bakery.Trains` | the namespace of every train a `Runs:` line names without a dot |
| `Actor:` | `Actor: customer "a customer"` | `actors.customer.noun` |
| `Constant:` | `Constant: EmptyBoard is "...."` | nothing: a value used by name in `Sets:`, `Example:` and `Starts with:` |
| `Type:` | `Type: Square` | nothing: a type a field names (`is a Square`) |
| `Machine:` | `Machine: answer-sheet`, or `Machine: shop "the shop"` | `machines.<id>`; its noun (or a system machine's name) is the id with hyphens as spaces unless quoted |
| `View:` | `View: "the shop sign"` | `views.<name>` |

### Under `Actor:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Roles:` | `Roles: Clerk, Manager` | `roles` |
| `Any signed-in` | alone on its line | `anyAuthenticated: true` |

An actor has one of the two, and is refused with both.

### Under `Type:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Values:` | `Values: "S", "M", "L"`, or a range: `Values: A to J, each followed by 1 to 10` | a string field with that `enum` (`A1` … `J10`) |
| `Has:` | `Has: postcode is a string, may be empty`, one per field | an object field with those `fields` |

A type is one or the other. A range is two letters or two whole numbers.

### Under `Machine:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Owned by:` | `Owned by: member, clerk` | `ownedBy`, a string for one actor and a list for several |
| `System` | alone | `system: "<id>/main"` |
| `System at:` | `System at: flight/tx101` | `system: "flight/tx101"` |
| `System keyed by:` | `System keyed by: shopId, tillId` | `system: { "keyedBy": [...] }` |
| `Started by:` | `Started by: "has an order"` | `start` |
| `Begins in:` | `Begins in: Draft` | `initial`; without it, the first state |
| `Version:` | `Version: 2` | `version` |
| `Migrates from:` | `Migrates from: 1 with Shop.Migrations.CheckoutFromVersion1` | `migrations."1"` |
| `Differential` | alone | `differential: true` |
| `Starts with:` | `Starts with: items is [], receipt is nothing` | `initialContext`, each value typed by its field |
| `Context:` | a field (see *Fields*), or `Context: target may be empty` | `context` (see *Context*) |
| `State:` | `State: Paid "is paid"`, or `State: Paid "is paid" committed` | `states.Paid` |
| `Trigger:` | `Trigger: Fire "fires at {square} in {subject}"` | `triggers.Fire` |
| `Guard:` | `Guard: hasLoaves when loaves is more than 0` | `guards.hasLoaves.rule` |
| `Runs:` | `Runs: FireShot in Firing`, or `Runs: Bakery.Trains.ITakePayment in Paying` | `invokes.Firing`, train `<Trains in>.IFireShotTrain` or the dotted name as written |
| `Link:` | `Link: "the receipt for {subject}" to order by orderId` | `links` |
| `Rule:` | `Rule: checkout:priced with Shop.Rules.TotalIsPriced, example true` | `rules` |
| `Reduction:` | `Reduction: checkout:add-item with Shop.Rules.AddItem, example items is ["bread"]` | `reductions` |

A machine has `Owned by:` or one of the three `System` lines, and is refused with both or neither.

### Under `State:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Requires:` | `Requires: receipt is not empty` (a rule, see *Rules*) | `requires` |
| `Context:` | a field, one per line; no `may be empty` form | the state's own `context`, in place of the machine's |

### Under `Trigger:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Input:` | a field, one per line: `Input: square is a Square` | `input` |
| `Sets:` | `Sets: target to square, loaves, shots to EmptyBoard` | `reduce: set`; a bare field is set from the input of its name |
| `Keeps` | alone | `reduce: keep` |
| `Clears` | alone | `reduce: clear` |
| `Resets` | alone | `reduce: reset` |
| `Reduces with:` | `Reduces with: checkout:add-item` | `reduce: custom` |
| `Guarded by:` | `Guarded by: hasLoaves` | `guard` |
| `Effect:` | `Effect: Shop.Effects.ICharge, key prefix "checkout:charge", example "rcpt-1"`; the key prefix may be left out | `effect` |

A trigger reduces once: one of `Sets:`, `Keeps`, `Clears`, `Resets` and `Reduces with:`.

### Under `Guard:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Holds:` | `Holds: "has something in it"` | `holds` |
| `Fails:` | `Fails: "has nothing in it"` | `fails` |

A guard that reads only the trigger's input may leave both out.

### Under `Runs:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Takes:` | `Takes: fleet, shots, owner` | `input`, each from the context field of its name; `owner` from `$owner` |
| `Input:` | `Input: square from target`, or `Input: memberId from the owner` | `input.square: "target"`, `input.memberId: "$owner"` |
| `Returns:` | `Returns: result is a string, shots is a string` | `output`, sorted by name |
| `Stores:` | `Stores: shots, result as yourShot` | a set step on every done outcome: `yourShot` from the output `result` |
| `Sets:` | `Sets: shots to EmptyBoard` | a set step on every done outcome, from an output field, a constant or a value |
| `Example:` | `Example: fleet is "f-1", board is EmptyBoard` | each done outcome's `examples` entry, where its `when` does not give the value |
| `Outcome:` | `Outcome: "the shot misses" when result is "miss"`, `… when it is done`, `… when it fails`, `… when it is called off` | `outcomes`, `done` with its `when`, `done`, `failed`, `cancelled` |

### Under `View:` and `Field:`

| Keyword | Form | Glossary |
| --- | --- | --- |
| `Reads:` | `Reads: order, shop` | `reads`; a system machine at a fixed key is read at it |
| `Field:` | `Field: status from game's state`, `Field: shot from game's yourShot` | `fields.status.from: "game.state"` |
| `Label:` (under `Field:`) | `Label: Dealing "Dealing the fleets"` | `labels.Dealing` |

## Fields

`<name> is a <type>`, then clauses after commas:

| Clause | Glossary |
| --- | --- |
| `may be empty` | `nullable: true`; without it a field is never null |
| `at least 1`, `at most 12` | `minimum`, `maximum` |
| `one of "S", "M", "L"` | `enum` |
| `where <rule>`, last | `constraints` |

Types: `a string`, `a number`, `an integer`, `a boolean`, `an object`, `a list`; the string formats `a uuid`,
`a date`, `a time`, `a date-time`, `a date-time-offset`, `a duration`, `a uri`, `a char`; a named `Type:`
(`a Square`, `an Address`); `a list of <type>` (`a list of strings`, `a list of Squares`); and
`a map of <type> to <type>` (`a map of string to integer`). Nothing nests inside a line: a record in a list is a named
type.

`Returns:` holds several fields on one line, separated by commas; a comma followed by a clause goes on with the field
before it.

## Rules

A rule is one comparison, or several joined by `and` or by `or`. A line that mixes the two is a problem: a rule that
needs both is a custom rule, written in C#, named in the lexicon (`checkout:priced holds`) and bound by a `Rule:`
line.

| Comparison | IR rule |
| --- | --- |
| `receipt is present`, `receipt is absent` | `present`, `absent` |
| `receipt is not empty` | `nonEmpty` |
| `receipt is a string` | `ofType` (string, number, integer, boolean, list, object) |
| `tags is a list of strings` | `arrayOf` |
| `size is one of "S", "M"` | `oneOf` |
| `result is "hit"` | `oneOf` with the one value: the IR has no string equality |
| `approved is true` | `boolEquals` |
| `loaves is 3`, `is at least 3`, `is at most 3`, `is more than 3`, `is less than 3` | `compare`, `eq` `gte` `lte` `gt` `lt` |
| `tags has 3 items`, `has at least 3 items`, `has fewer than 3 items`, … | `count` |
| `code is 3 characters long`, `is at least 3 characters long`, … | `length` |
| `checkout:priced holds` | `custom` |

A field is read from the context in `Requires:`, `Guard:` and `where`, and from what the train returns in an
outcome's `when`. `input item` and `context item` say which, where it is not the default (a guard on the trigger's
input reads `input item is not empty`).

## Values

Typed by the field they are for. A string is quoted, a number and an integer bare (an integer whole), a boolean `true`
or `false`, a list in brackets (`["bread", "rye"]`), and `nothing` is null, allowed only where the field may be empty.
A bare name is a constant, or, in `Sets:` and `Stores:`, a field of what the line reads from. A value of the wrong
type is a problem at its line. An object field takes no value in a lexicon; set it with a custom reduction.

## What is inferred

- **A machine's noun** (or a system machine's name) is its id with hyphens as spaces, unless quoted after the id. A
  system machine at a fixed key is at `<id>/main` unless `System at:` says otherwise.
- **Context.** Each field a `Context:` line declares, in the order written, then each field something writes and no
  line declares, sorted by name, with the type of what writes it: a trigger's input, a train's output, a constant or a
  value. A written field is never null unless a `Context: <field> may be empty` line says so. A field nothing writes
  is declared with its type. Two writes that disagree on the type are a problem at the second.
- **A train's full name.** A dotted name is taken as written; a plain one is `I<Name>Train` in the namespace of
  `Trains in:`, which is then required.
- **A train's input.** Each `Takes:` field from the context field of its name, `owner` from the instance owner, and
  each `Input:` line as written, in the order written. Given the train assemblies, every input the record has and no
  line writes is wired by the same rule, and `Takes:` is optional.
- **A train's output.** From `Returns:`, sorted by name; given the train assemblies, from the output record, as the
  host reads it, and `Returns:` is optional. A `Returns:` that disagrees with the record is a problem at its line.
- **Each done outcome** stores what `Stores:` and `Sets:` say, and its example is the value its `when` pins for each
  output field, then the `Example:` line's. An output field with neither is a problem at the outcome.
- **A view's reads** of a system machine at a fixed key carry the key.

The generated `glossary.json` is committed, so a reader can see what was filled in.

## The canonical glossary

The elaborated glossary is written in one form, so it can be compared byte for byte: each object's keys in the order
the schemas list them, every map in the order it was declared, two spaces of indent, a trailing newline, and nothing
escaped that JSON does not require (an apostrophe and non-ASCII text are written as they are).
