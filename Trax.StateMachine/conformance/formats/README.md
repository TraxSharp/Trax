# Feature formats corpus

Documents in the two formats a machine derived from features is written in, the glossary
(`../glossary.schema.json`) and the host file (`../host.schema.json`). Each format has three directories:

| Directory | The schema | The C# reader |
| --- | --- | --- |
| `valid/` | accepts | accepts |
| `invalid/` | refuses | refuses |
| `refused/` | accepts | refuses: names in it disagree (an owner that is not an actor, a phrase two steps could mean), which a schema cannot say, or a key given twice, which `JSON.parse` drops silently |

`FormatCorpusTests` (C#) and `src/formatSchemas.conformance.test.ts` (TypeScript) fail on a file in the wrong
directory, so the reader and the schema cannot drift. A new rule in either gets a file here in the same change.
Name a file for the one thing wrong with it.
