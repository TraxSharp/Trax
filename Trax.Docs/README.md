# Trax.Docs

[![Deploy](https://github.com/TraxSharp/Trax.Docs/actions/workflows/trigger-website-deploy.yml/badge.svg?branch=main&event=push)](https://github.com/TraxSharp/Trax.Docs/actions/workflows/trigger-website-deploy.yml?query=branch%3Amain)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax.Docs/blob/main/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs)

> Part of [Trax](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [All repos](https://github.com/TraxSharp)

Trax.Docs holds the documentation for Trax, published at [traxsharp.net/docs](https://traxsharp.net/docs), and the
decision records for the rules that span more than one Trax repo.

## What is here

| Path | What it holds |
|---|---|
| `*.md` and the section directories (`core/`, `effect/`, `mediator/`, `scheduler/`, `statemachine/`, `sdk-reference/`, `reference/`, ...) | The pages published at traxsharp.net/docs. A file at `effect/metadata.md` is served at `/docs/effect/metadata`. |
| `adr/` | Architecture decision records that bind more than one repo, indexed in [`adr/README.md`](https://github.com/TraxSharp/Trax.Docs/blob/main/adr/README.md). Not published to the site. |
| `tools/Trax.Adr.Guard` | The checker for ADR frontmatter, index tables and guard census. It runs on this corpus in CI and is shipped to the code repos as the `adr-guard` composite action in `.github/actions/adr-guard`. A guard release is a `vX.Y.Z` tag cut by hand with the **Release ADR Guard** workflow, which Dependabot in the code repos picks up ([ADR 0038](https://github.com/TraxSharp/Trax.Docs/blob/main/adr/0038-the-adr-guard-is-released-by-tag.md)). |
| `tests/Trax.Docs.Tests` | Lint for the pages: internal links resolve, no em dashes, no Jekyll syntax, SDK reference blocks are well formed. |

A push to `main` that touches anything outside `adr/`, `.claude/`, `tools/`, `tests/` and `.github/` triggers a deploy of [Trax.Website](https://github.com/TraxSharp/Trax.Website),
which renders these files.

## Preview locally

The site is built by Trax.Website, which reads the docs from a sibling `Trax.Docs` directory when it finds one:

```bash
git clone https://github.com/TraxSharp/Trax.Docs.git
git clone https://github.com/TraxSharp/Trax.Website.git
cd Trax.Website
npm ci
npm run dev
```

`npm run dev` runs `scripts/sync-docs.sh`, which copies the Markdown from `../Trax.Docs` (your working copy, whatever
branch it is on) into the site before starting it, so restart it to pick up an edit. Without a sibling checkout the script
clones `main` from GitHub instead.

## Checks

```bash
dotnet test
```

Pull requests run the same tests, a CSharpier check and the ADR guard.

## Writing

Read [reference/contributing-docs](https://github.com/TraxSharp/Trax.Docs/blob/main/reference/contributing-docs.md)
before writing a page. It sets the voice, the link format (`/docs/<path>`), and the rule that a code change and its docs
change ship together. Report vulnerabilities privately as described in
[SECURITY.md](https://github.com/TraxSharp/Trax.Docs/blob/main/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
