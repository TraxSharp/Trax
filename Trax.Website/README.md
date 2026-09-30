# Trax.Website

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax.Website/blob/main/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs)

> Part of [Trax](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [All repos](https://github.com/TraxSharp)

Trax.Website is the source for [traxsharp.net](https://traxsharp.net): the landing page, and the docs site rendered from
[Trax.Docs](https://github.com/TraxSharp/Trax.Docs), with raw Markdown and `llms.txt` for agents.

## Stack

| Piece | What it does |
|---|---|
| Next.js 16 (App Router) | Server-rendered site. It is not a static export: `next.config.ts` rewrites `/docs/<slug>.md` to the raw Markdown route. |
| Tailwind CSS 4 | Styling, dark theme |
| `next-mdx-remote`, `rehype-pretty-code` and Shiki | Render the docs Markdown with highlighted code |
| Trax.Docs | The page content, synced in at dev and build time |

## Run it locally

```bash
git clone https://github.com/TraxSharp/Trax.Website.git
cd Trax.Website
npm ci
npm run dev
```

`npm run dev` and `npm run build` both run `scripts/sync-docs.sh` first. It copies every `.md` file from a sibling
`../Trax.Docs` checkout into `.docs-cache/` (gitignored), skipping `README.md`, `adr/`, `.claude/`, `tools/`, `tests/`
and `.github/`. With no sibling checkout it shallow-clones `main` from GitHub instead. To preview a docs change, clone
Trax.Docs next to this repo, edit it there, and restart the dev server.

Before committing, run `npm run lint` and `npm run build`.

## Project structure

```
src/
├── app/
│   ├── page.tsx                  # landing page
│   ├── docs/                     # docs home and [...slug] pages
│   ├── docs-markdown/[...slug]/  # raw Markdown, served at /docs/<slug>.md
│   ├── llms.txt/                 # /llms.txt index for agents
│   ├── llms-full/[bundle]/       # full docs text in per-section bundles
│   ├── robots.ts, sitemap.ts
│   └── layout.tsx, not-found.tsx
├── components/
│   ├── landing/                  # landing page sections
│   ├── docs/                     # docs layout, sidebar, breadcrumb, table of contents
│   ├── layout/                   # header, footer, mobile nav
│   └── mdx/                      # MDX component overrides
└── lib/
    ├── docs.ts                   # reads .docs-cache, front matter, page summaries
    ├── nav-tree.ts               # sidebar tree
    ├── llms.ts                   # llms.txt and the full-text bundles
    ├── mdx-options.ts            # remark and rehype plugins shared by docs pages
    └── site.ts                   # site URL and description
scripts/
└── sync-docs.sh                  # copies Trax.Docs into .docs-cache
```

## License

MIT. There is no commercial edition, and there will not be one.

Trax is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
