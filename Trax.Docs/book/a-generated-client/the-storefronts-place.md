---
title: "15.2 The storefront's place"
description: "Chapter 15 of the Trax book, section 2: The storefront's place."
type: tutorial
area: general
parent: "15. A generated client"
grand_parent: The Trax book
nav_order: 2
snippet_context: none
follows: book/a-generated-client/how-it-fits-together
---

# 15.2 The storefront's place

The storefront is `web/`, beside the hub, a Vite and React project in TypeScript.

```text
Shopfront/
├── Shopfront.Core/
├── Shopfront.Hub/
├── Shopfront.Worker/
├── machines/
├── tests/
└── web/
    ├── package.json
    ├── tsconfig.json
    ├── vite.config.ts
    ├── index.html
    └── src/
        ├── graphql/        the documents the page writes
        ├── trax/           what trax client generates; committed, never edited
        ├── connection.ts
        ├── main.tsx
        ├── App.tsx
        ├── Catalogue.tsx
        ├── OrderProgress.tsx
        └── MyOrders.tsx
```

```json file=web/package.json
{
  "name": "shopfront-web",
  "private": true,
  "version": "0.0.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build"
  },
  "dependencies": {
    "@traxsharp/state-machine": "1.67.0",
    "graphql": "^16.10.0",
    "graphql-ws": "^6.0.0",
    "react": "^19.3.0",
    "react-dom": "^19.3.0"
  },
  "devDependencies": {
    "@types/react": "^19.3.0",
    "@types/react-dom": "^19.3.0",
    "@vitejs/plugin-react": "^6.1.2",
    "typescript": "~5.9.3",
    "vite": "^8.3.3"
  }
}
```

`@traxsharp/state-machine` is the runtime the generated client is built on, released with the
NuGet packages at the same version. `graphql-ws` is the socket a subscription runs on.

```json file=web/tsconfig.json
{
  "compilerOptions": {
    "target": "ES2022",
    "lib": ["ES2022", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "moduleResolution": "bundler",
    "verbatimModuleSyntax": true,
    "resolveJsonModule": true,
    "noEmit": true,
    "jsx": "react-jsx",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "skipLibCheck": true
  },
  "include": ["src"]
}
```

```ts file=web/vite.config.ts
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The hub allows requests from http://localhost:5173 only, so the dev server never moves to another port.
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, strictPort: true },
});
```

```html file=web/index.html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Shopfront</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

```text file=web/.gitignore
node_modules
dist
```

## SDK Reference

> [Generated clients](/docs/statemachine/call-a-machine-from-typescript) | [trax client](/docs/reference/cli) | [Client manifest](/docs/sdk-reference/statemachine-api/client-manifest) | [Fetch only what a component needs](/docs/api/fetch-only-what-a-component-needs) | [Run progress](/docs/sdk-reference/graphql-api/run-progress)

---

Next: [15.3 The documents](/docs/book/a-generated-client/the-documents)
