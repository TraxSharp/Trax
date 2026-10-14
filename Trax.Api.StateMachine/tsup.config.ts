import { readFileSync } from 'node:fs';
import { defineConfig } from 'tsup';

// The version npm packs, which a release writes into package.json before it builds (src/version.ts).
const { version } = JSON.parse(readFileSync(new URL('./package.json', import.meta.url), 'utf8'));

// Ship a real build (dual ESM/CJS + type declarations), not raw .ts — so consumers don't have to add the
// package to their bundler's transpile list. React stays external behind the ./react subpath.
export default defineConfig({
  entry: { index: 'src/index.ts', react: 'src/react.ts', 'client/index': 'src/client/index.ts' },
  format: ['esm', 'cjs'],
  dts: true,
  clean: true,
  sourcemap: true,
  external: ['react', 'valibot'],
  define: { __TRAX_ENGINE_VERSION__: JSON.stringify(version) },
});
