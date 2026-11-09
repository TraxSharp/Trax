# Feature formats corpus

Documents in the two formats a machine derived from features is written in, the glossary
(`../glossary.schema.json`) and the host file (`../host.schema.json`). Each format has three directories:

| Directory | The schema | The C# reader |
| --- | --- | --- |
| `valid/` | accepts | accepts |
| `invalid/` | refuses | refuses |
| `refused/` | accepts | refuses: names in it disagree (an owner that is not an actor, a phrase two steps could mean), which a schema cannot say, a key given twice, which `JSON.parse` drops silently, or an escaped lone surrogate, which is not text |

`FormatCorpusTests` (C#) and `src/formatSchemas.conformance.test.ts` (TypeScript) fail on a file in the wrong
directory, so the reader and the schema cannot drift. A new rule in either gets a file here in the same change.
Name a file for the one thing wrong with it.

## The lexicon

`lexicon/` holds the lexicon (`../lexicon.md`), the file people write and Trax elaborates to the glossary. It is a
.NET-only format: no JSON Schema, no TypeScript reader, so neither column above applies and `LexiconTests` (C#) is the
only test that reads it.

| Directory | What it holds |
| --- | --- |
| `lexicon/valid/` | a lexicon that elaborates, byte for byte, to the glossary of its name: `<name>.json` beside it, else `glossary/valid/<name>.json`. Every glossary in `glossary/valid/` has one. |
| `lexicon/invalid/` | a lexicon refused for one thing, its first line `# expect <line>: <words of the problem>`; a directory is read as a features directory (two lexicons in one). |

The invalid lexicons are their own list, not a copy of `glossary/invalid/`: most of those are JSON shapes a lexicon
cannot write. What the glossary reader still refuses on the elaborated form (an owner that is not an actor, two
phrases for one step) has a lexicon here, to show the problem comes back at its lexicon line.
