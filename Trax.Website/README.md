# Trax.Website

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax/blob/main/Trax.Website/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs)

> Part of [Trax .NET](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [Repository](https://github.com/TraxSharp/Trax)

Trax.Website is the source for [traxsharp.net](https://traxsharp.net): the landing page, the docs site rendered from
[Trax.Docs](https://github.com/TraxSharp/Trax/tree/main/Trax.Docs), with raw Markdown and `llms.txt` for agents, and a demo of the
operations dashboard, whose source is in [`dashboard/`](dashboard/README.md) for now.

## Stack

| Piece | What it does |
|---|---|
| Next.js 16 (App Router) | Server-rendered site. It is not a static export: `next.config.ts` rewrites `/docs/<slug>.md` to the raw Markdown route. |
| Tailwind CSS 4 | Styling, dark theme |
| `next-mdx-remote`, `rehype-pretty-code` and Shiki | Render the docs as CommonMark (not MDX) with highlighted code; raw HTML is limited to a few tags by `rehype-sanitize` |
| Trax.Docs | The page content, synced in at dev and build time |
| `dashboard/` | The operations dashboard (Vite, React, urql), its own npm project; its demo build is served at `/dashboard/demo/` |

## Run it locally

```bash
git clone https://github.com/TraxSharp/Trax.git
cd Trax/Trax.Website
npm ci
npm run dev
```

`npm run dev`, `npm run build` and `npm test` all run `scripts/sync-docs.sh` first. It copies every `.md` file from
`../Trax.Docs` in the same checkout into `.docs-cache/` (gitignored), skipping `README.md`, `adr/`, `.claude/`, `tools/`,
`tests/` and `.github/`. To preview a docs change, edit it in `Trax.Docs` and restart the dev server. A production build
also stamps the latest release's version into every `<PackageReference Include="Trax.*" Version="...">` a page shows
(`TRAX_DOCS_VERSION` overrides it), so the site always names a version a reader can install.

The ADRs are not published, so the sync also writes `.docs-cache/adr-index.json`, the ADR file names in Trax.Docs and
in each code folder. A citation such as `Trax.Mediator/docs/adr/0004` in a page links to that file on GitHub, or to the
folder's ADR index when the file is not known.

## Deploying

traxsharp.net is built by Vercel from the `website` branch, its production branch, with `Trax.Website` as the
project's root directory. Nothing pushes to that branch by hand: the repository's Release workflow moves it to each
release tag once the packages are on nuget.org, so the published docs describe released code
([ADR 0043](../Trax.Docs/adr/0043-the-site-is-published-from-a-release.md)). To publish a docs fix that describes
nothing unreleased, dispatch the Website workflow with `main`.

Vercel builds nothing else: `ignoreCommand` in `vercel.json` skips every branch except `website` and `website/*`. Push
a branch named `website/<something>` to get a preview deployment; CI builds the site on every pull request that touches
it either way.

The docs are rendered as CommonMark with GFM, not MDX: a `>` is a blockquote, and JSX and `{expressions}` are not
available. Raw HTML is limited to a short allow-list (`<a id>` anchors and a few inline tags) and sanitized; any other
tag renders as the text written. A page's `description:` front matter, when present, is its meta description and its
line in `llms.txt`; without it the first prose paragraph is used.

`npm run dev` and `npm run build` also run `scripts/build-dashboard-demo.sh`, which installs the dashboard's own
dependencies from its lockfile (locked, no install scripts) when they are missing or stale, builds its demo, and copies it
to `public/dashboard/demo` (gitignored). `/dashboard` frames it; `next.config.ts` lets only that page frame it and
answers any path under it with its `index.html`, so the dashboard's routes reload.

Before committing, run `npm run lint`, `npm test` and `npm run build`, and when `dashboard/` changed, its own
`npm run lint`, `npm test`, `npm run build` and `npm run build:demo` in that folder. `npm test` syncs the docs, renders Markdown
through the site's pipeline and checks what reaches the HTML (`tests/render.test.ts`), and renders every published page
(`tests/pages.test.ts`), and plays every recorded sample run through its player
(`tests/*-replay.test.ts`). It needs Node 22.18 or later.

## Project structure

```
src/
├── app/
│   ├── page.tsx                  # landing page
│   ├── dashboard/                # the dashboard tab: a short intro and the demo, framed
│   ├── docs/                     # docs home and [...slug] pages
│   ├── docs-markdown/[...slug]/  # raw Markdown, served at /docs/<slug>.md
│   ├── llms.txt/                 # /llms.txt index for agents
│   ├── llms-full/[bundle]/       # full docs text in per-section bundles
│   ├── robots.ts, sitemap.ts
│   └── layout.tsx, not-found.tsx
├── components/
│   ├── landing/                  # landing page sections; SeeItRunning.tsx tabs the samples, *Replay.tsx play them
│   ├── docs/                     # docs layout, sidebar, breadcrumb, table of contents
│   ├── layout/                   # header, footer, mobile nav
│   └── mdx/                      # element overrides for rendered docs
├── data/
│   └── *-recordings.json         # recorded runs of the samples, which the landing page plays back
└── lib/
    ├── docs.ts                   # reads .docs-cache, front matter, page summaries
    ├── nav-tree.ts               # sidebar tree
    ├── llms.ts                   # llms.txt and the full-text bundles
    ├── mdx-options.ts            # docs rendering: CommonMark, raw HTML allow-list, sanitizer, ADR links, highlighting
    ├── *-replay.ts               # one player per sample: recorded events in, what its page shows out
    └── site.ts                   # site URL and description
scripts/
├── sync-docs.sh                  # copies Trax.Docs into .docs-cache
├── build-dashboard-demo.sh       # builds dashboard/'s demo into public/dashboard/demo
└── adr-index.mjs                 # lists the ADR files that citations link to
dashboard/                        # the operations dashboard, its own npm project (see dashboard/README.md)
tests/
├── render.test.ts                # what the docs pipeline lets through to the HTML
├── pages.test.ts                 # renders every published page through the pipeline
└── *-replay.test.ts              # plays every recording through its sample's player
```

## See it running

The **See it running** section has a tab per sample: Recovery, State machine, Chat over WebSockets, SignalR and
Persisted operations. Each tab plays the real sample in the browser, and the reader can drive it: crash a Recovery
run, fire the checkout's triggers in any order, pick the next chat line, start pings, call and hot-fix persisted
operations. Nothing runs a server. Each `src/data/<sample>-recordings.json` holds what the sample's host actually
sent, recorded by Trax.Samples' `scripts/recordings` with every request, response and pushed event; where a sample
takes free input, presets were recorded instead. Under each demo, `SampleSource` links the real C# on GitHub and
gives the commands to start it.

Re-record a sample with its script's `--copy-to ../../../Trax.Website/src/data`, from a sibling Trax.Samples
checkout, when the sample changes; `npm test` plays every recording through its player.

## The dashboard

The **Dashboard** tab serves the operations dashboard on recordings of three sample hosts (Scheduling, Recovery and
Persisted operations), made by Trax.Samples' `scripts/recordings/dashboard.mjs` into
`dashboard/src/demo/data`. Every page, filter, row and button works; writes stay in the visitor's tab. Re-record it with
that script's `--copy-to ../../../Trax.Website/dashboard/src/demo/data`, then run `npm test` in `dashboard/`, which
fails on any page of the demo the recording does not answer. [dashboard/README.md](dashboard/README.md#the-demo) says
how it answers.

## License

MIT. There is no commercial edition, and there will not be one.

Trax .NET is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
