---
authors: [Theauxm]
repos: [statemachine, cli, samples, docs, website]
areas: [platform, docs]
status: accepted
---

# The lexicon is the source, and the glossary is derived from it

People wrote the glossary of [0058](./0058-features-and-a-glossary-are-a-source-of-machines.md) by hand, and it was the
elaborated form: Battleship's was 678 lines of JSON, of which about 60 were words a person chose, and the rest
defaults, repetition and restated train records. A team now writes a **lexicon**, a short line-based file in the style
of Gherkin, and Trax elaborates it to the glossary, which keeps its name and schema and becomes a derived file like
the IRs and `host.json`. This amends 0058: where it says a team writes the glossary, the team writes the lexicon.

## Status

**Accepted.**

## Decisions

- **The lexicon is line-based, strict on structure and indifferent to layout.** Every line starts with a keyword from
  a closed set, each keyword has one form and belongs under one kind of block, and whitespace outside quotes never
  matters. A mistyped keyword is a problem with a suggestion, never a comment, because there is no free text; notes
  go after `#`. Nothing is skipped or read in part. The grammar is `Trax.StateMachine/conformance/lexicon.md`.
  - *Rejected:* structured Markdown. Its structure is too free (a heading with a typo is still Markdown), tables are
    tedious to edit, and the first sketch had a sentence carrying meaning, which means parsing English.
- **Wiring that varies is written, wiring that follows a rule is not.** A context field's type comes from what
  writes it, a train input from the context field of its name, `owner` from the instance owner, a plain train name is
  `I<Name>Train` in the `Trains in:` namespace, each done outcome's example from its `when` and the `Example:` line.
  What a store writes (`Stores:`) is always written out, so the wiring stays visible. Context fields are never null
  unless a line says they may be empty.
- **Rules are flat.** One connective per line, `and` or `or`; a rule that needs both is a custom rule written in C#.
  Values are typed by the field they are for, so a string is quoted and a boolean is not.
- **The generated glossary is committed, in `machines/`.** `trax machine from-gherkin` writes `glossary.json` beside
  the IRs, `host.json` and `access.txt`, in one canonical form (schema key order, declaration order for maps), so a
  reader can see what was filled in and `--check` fails when it is stale. `features/` holds only what people write.
  Every command elaborates the lexicon on each run; a lexicon beside a `glossary.json`, or two in one directory, is
  refused. A JSON glossary given with `--glossary` is still read, for apps not yet moved.
- **Every problem names a lexicon line.** The elaborated glossary goes through the glossary reader and derivation as
  before, and each problem they raise about a key is mapped back to the lexicon line that wrote it. No problem a
  person sees names `glossary.json`.
- **Train fields come from `Returns:` lines, or from the train assemblies.** Without assemblies, a `Runs:` block
  restates the train's output (`Returns:`) and the inputs it reads by name (`Takes:`). Given the assemblies (the
  in-process tests pass them, the CLI takes `--trains <dll>`), both are read from the records as the host reads them,
  the lines become optional, and one that disagrees with the record is a problem at its line.
  - *Rejected:* a manifest of trains exported by the hub. Exporting starts the host, which refuses to start until
    `machines/` is valid, which is circular.
- **The lexicon is a .NET-only format.** It has no JSON Schema and no TypeScript reader: every engine reads the
  glossary, and only the .NET tooling writes it.

## Exemplars

- `LexiconTests` in `Trax.StateMachine.Tests` holds the corpus under `conformance/formats/lexicon/`: each valid
  lexicon elaborates to its glossary byte for byte, every valid glossary has a lexicon and is in the canonical form,
  each invalid one is refused at its line, layout never changes the result, and a problem derivation finds with the
  glossary is reported at its lexicon line.
- `LexiconTrainRecordTests` there holds the train assemblies: output and input read from the records, a renamed
  output field refused at the `Stores:` line that names it, a `Returns:` that disagrees refused at its line.
- `FromGherkinCommandTests` in `Trax.Cli.Tests` holds the commands: `glossary.json` written to `--out` and checked,
  refused beside a lexicon and for two lexicons, every command reading the lexicon. `LexiconFormatCommandTests` holds
  `trax lexicon format`: a second run changes nothing, and the glossary is the same before and after.
- `InMemoryRunTests` (CheckIn) and `ReturnFeaturesTests` (Shopfront) gate each sample's committed glossary, with the
  train's records checked against the lexicon's `Returns:` line.

Not covered: the website (its feature walk and the home prompt) still teaches the JSON until a release carries the
CLI that reads lexicons.

## Changelog

- **2026-11-09**: Recorded.
