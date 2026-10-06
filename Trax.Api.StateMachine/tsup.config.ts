import { defineConfig } from 'tsup';

// Ship a real build (dual ESM/CJS + type declarations), not raw .ts — so consumers don't have to add the
// package to their bundler's transpile list. React stays external behind the ./react subpath.
export default defineConfig({
  entry: { index: 'src/index.ts', react: 'src/react.ts', 'client/index': 'src/client/index.ts' },
  format: ['esm', 'cjs'],
  dts: true,
  clean: true,
  sourcemap: true,
  external: ['react', 'valibot'],
});
