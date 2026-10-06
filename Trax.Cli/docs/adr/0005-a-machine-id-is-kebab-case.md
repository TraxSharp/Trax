---
authors: [Theauxm]
areas: [tooling]
status: accepted
---

# A machine id is kebab-case, and `trax machine` refuses any other

A state machine's id (what `Id(...)` sets) names every artifact `trax machine generate` and
`check` write: `<id>.ir.json`, `<id>.contexts.g.ts`, `<id>.machine.g.ts`. The CLI accepts only
lowercase ASCII words of letters and digits joined by single hyphens, starting with a letter
(`checkout`, `write-to-congress`), and refuses anything else before writing a file. That is the
form `trax machine new` already produces.

## Status

**Accepted.**

## Considered options

**Refuse only what escapes the output directory** (`/`, `\`, `..`, an empty id). Rejected: it
leaves ids that are legal paths but not portable ones (case-only differences on macOS and
Windows, characters the TypeScript module specifier or a shell script trips over), and a rule
stated as "anything but these" has to be re-derived by every reader.

**Sanitize the id into a file name.** Rejected for the same reason cli/0002 refuses schema names
rather than rewriting them: the file names are the contract the twin's imports and the committed
artifacts depend on, and a rewritten id could make two machines write one file.

## Consequences

A machine whose id is not kebab-case can no longer be generated until its `Id(...)` changes, and
changing an id renames its committed artifacts. No id in the Trax repositories or samples is
affected. `Id(...)` in Trax.Effect does not check the id yet, so a machine can still be built and
run with an id the CLI refuses; the CLI is the first place the rule is enforced.

## Exemplars

- `MachineGeneratorTests` pins it: an id with `/` or `..`, an empty one, one with an upper-case
  letter or an underscore is refused with nothing written and node never run, and kebab-case
  ids are accepted.
- [CLI](/docs/reference/cli#trax-machine-generate) states the rule.

Not covered: `Fluent.Id()` in Trax.Effect, which stores the id unchecked.

## Changelog

- **2026-09-28**: Recorded.
