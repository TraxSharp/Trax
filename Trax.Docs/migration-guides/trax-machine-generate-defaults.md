---
title: Regenerate twins after the trax machine generate defaults changed
description: "What changed in trax machine generate's defaults when the engine shipped on npm, and how to regenerate a twin or keep generating it as before."
type: how-to
area: upgrading
parent: Upgrading
nav_order: 1
---

# Regenerate twins after the trax machine generate defaults changed

Applies from: Trax 1.63.1.

The defaults of [`trax machine generate`](/docs/reference/cli#trax-machine-generate) changed when the engine began
shipping inside the tool and on npm as `@traxsharp/state-machine`:

- **Without `--engine-src`** the twin now imports the installed package (`--import-style specifier`). Earlier
  versions required `--engine-src` and imported the engine by relative path. With `--engine-src` the default is
  still relative.
- **The default specifier is `@traxsharp/state-machine`.** It used to be `state-machine` in the `@trax` scope,
  which Trax does not own on npm. If you vendor the engine behind another name or path alias, pass it with
  `--specifier <module>`.
- A specifier twin's header now names the import it was generated with, and its IR records the IR format. Both
  are new lines in the generated file, so the first regeneration after upgrading shows them as a diff.

## Regenerate with the new defaults

1. Install `@traxsharp/state-machine` in the frontend that imports the twin.
2. Run `trax machine generate` without `--engine-src`, and commit the regenerated twin, IR and corpus.
3. Run `trax machine check` in CI as before; it fails on a twin left from the previous tool.

## Keep generating as before

Pass `--import-style relative --engine-src <engine/src>` for a relative twin, or
`--import-style specifier --specifier <module>` for one importing your own name for the engine.

Why the twin records the IR format, and what the installed package checks on load, is in
[The codegen pipeline](/docs/statemachine/codegen-pipeline#generation-and-drift).
