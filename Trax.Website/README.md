# Trax.Website

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Source for [traxsharp.net](https://traxsharp.net), the documentation and marketing site for the Trax .NET framework.

## Stack

- **Next.js 15** (App Router, static export)
- **Tailwind CSS 4** (dark theme)
- **Shiki** for server-side syntax highlighting
- **MDX** via `next-mdx-remote` for docs and blog rendering
- **Trax.Docs** as content source (local workspace or cloned at build time)

## Getting Started

```bash
git clone https://github.com/TraxSharp/Trax.Website.git
cd Trax.Website
npm install
npm run dev
```

If you're working in the [Trax monorepo workspace](https://github.com/TraxSharp), the dev server automatically picks up docs from `../Trax.Docs/`. Otherwise it clones the latest from GitHub.

## Docs Sync

Documentation markdown lives in [Trax.Docs](https://github.com/TraxSharp/Trax.Docs). The sync script runs automatically on `npm run dev` and `npm run build`:

1. Uses the local workspace (`../Trax.Docs/`) if it exists
2. Otherwise shallow-clones `main` from GitHub
3. Copies all `.md` files into `.docs-cache/` (gitignored)

## Project Structure

```
src/
├── app/              # Next.js pages (landing, docs, blog, demo, about)
├── components/
│   ├── landing/      # Landing page sections
│   ├── docs/         # Docs layout, sidebar, table of contents
│   ├── blog/         # Blog card components
│   ├── demo/         # Live demo widgets (connects to Trax GraphQL)
│   ├── layout/       # Header, footer, mobile nav
│   └── mdx/          # Custom MDX components
├── lib/
│   ├── docs.ts       # Markdown parsing, frontmatter, Jekyll transforms
│   ├── nav-tree.ts   # Sidebar navigation tree builder
│   ├── blog.ts       # Blog post loading
│   └── graphql/      # GraphQL client, queries, subscriptions
content/
└── blog/             # Blog posts (MDX)
scripts/
└── sync-docs.sh      # Docs sync script
```

## Environment Variables

Copy `.env.example` to `.env.local` and fill in:

| Variable | Description |
|----------|-------------|
| `NEXT_PUBLIC_GRAPHQL_URL` | Trax GraphQL endpoint (default: `http://localhost:5200/trax/graphql`) |
| `NEXT_PUBLIC_GRAPHQL_API_KEY` | API key for the GraphQL endpoint |

These are only needed for the `/demo` page. The rest of the site is fully static.

## License

[MIT](LICENSE)

## Trademark & Brand Notice

Trax is an open-source .NET framework provided by TraxSharp. This project is an independent community effort and is not affiliated with, sponsored by, or endorsed by the Utah Transit Authority, Trax Retail, or any other entity using the "Trax" name in other industries.
