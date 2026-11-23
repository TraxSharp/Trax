# Third-party notices

Trax.Analyzers contains code copied from the projects below. Each is MIT licensed; the full license
text, with its copyright notice, is in `licenses/` and ships in the package.

| Project | What was copied | License |
|---|---|---|
| [Meziantou.Analyzer](https://github.com/meziantou/Meziantou.Analyzer) | the rules, code fixes, suppressors, annotations, tests and rule pages | [licenses/Meziantou.Analyzer.txt](licenses/Meziantou.Analyzer.txt) |
| [Meziantou.Framework.Roslyn](https://github.com/meziantou/Meziantou.Framework) | the Roslyn helpers the rules are written against (`src/Trax.Analyzers.Roslyn`) | [licenses/Meziantou.Framework.Roslyn.txt](licenses/Meziantou.Framework.Roslyn.txt) |
| [Meziantou.Polyfill](https://github.com/meziantou/Meziantou.Polyfill) | the polyfills its generator wrote for netstandard2.0 (`src/Trax.Analyzers.Polyfills`) | [licenses/Meziantou.Polyfill.txt](licenses/Meziantou.Polyfill.txt) |

The versions copied are recorded in `upstream.txt` and `tools/generate-polyfills.sh`. The copy
renames namespaces and rule ids; `tools/sync-upstream.sh` does the copy and lists every rename.
