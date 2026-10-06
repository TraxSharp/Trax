# Trax.Docs

[![CI](https://github.com/TraxSharp/Trax/actions/workflows/ci.yml/badge.svg?branch=main&event=push)](https://github.com/TraxSharp/Trax/actions/workflows/ci.yml?query=branch%3Amain)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs)

> Part of [Trax .NET](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [Repository](https://github.com/TraxSharp/Trax)

Trax.Docs holds the documentation for Trax, published at [traxsharp.net/docs](https://traxsharp.net/docs), and the
decision records for the rules that span more than one folder of the [Trax repository](https://github.com/TraxSharp/Trax).

## What is here

| Path | What it holds |
|---|---|
| `*.md` and the section directories (`core/`, `effect/`, `mediator/`, `scheduler/`, `statemachine/`, `sdk-reference/`, `reference/`, ...) | The pages published at traxsharp.net/docs. A file at `effect/metadata.md` is served at `/docs/effect/metadata`. |
| `adr/` | Architecture decision records that bind more than one folder, indexed in [`adr/README.md`](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/README.md). Not published to the site. |
| `tools/Trax.Adr.Guard` | The checker for ADR frontmatter, index tables and guard census. CI builds it from this folder in the same checkout and runs it over each folder's ADR corpus through the local composite action `.github/actions/adr-guard` at the repository root, so a guard change and the corpus it checks land in one pull request ([ADR 0042](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0042-trax-is-one-repository-and-releases-at-one-version.md)). |
| `tests/Trax.Docs.Tests` | Lint for the pages: internal links resolve, no em dashes, no Jekyll syntax, SDK reference blocks are well formed. |

[Trax.Website](https://github.com/TraxSharp/Trax/tree/main/Trax.Website) renders these files. The site is published from a
release: the release workflow moves the `website` branch to each release tag, so the published docs describe released code
([ADR 0043](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/adr/0043-the-site-is-published-from-a-release.md)). A docs
change lands in the same pull request as the code it describes.

## Preview locally

The site is built by Trax.Website, which reads the docs from the `Trax.Docs` folder of the same checkout:

```bash
git clone https://github.com/TraxSharp/Trax.git
cd Trax/Trax.Website
npm ci
npm run dev
```

`npm run dev` runs `scripts/sync-docs.sh`, which copies the Markdown from `Trax.Docs` (your working copy, whatever branch
it is on) into the site before starting it, so restart it to pick up an edit. A production build also stamps the latest
release version into every `Trax.*` `PackageReference` a page shows.

## Checks

```bash
dotnet test
```

Pull requests run the same tests, a CSharpier check and the ADR guard.

## Writing

Read [reference/contributing-docs](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/reference/contributing-docs.md)
before writing a page. It sets the voice, the link format (`/docs/<path>`), and the rule that a code change and its docs
change ship together. Report vulnerabilities privately as described in
[SECURITY.md](https://github.com/TraxSharp/Trax/blob/main/Trax.Docs/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax .NET is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
