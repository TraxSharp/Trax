import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

// The package installs nothing of its own. What a page needs beside it (react, graphql-ws) is a peer the app brings,
// so the package can never add a second copy, or a package the app did not choose, to its install.

const here = path.dirname(fileURLToPath(import.meta.url));
const manifest = JSON.parse(readFileSync(path.join(here, '..', 'package.json'), 'utf8')) as {
  dependencies?: Record<string, string>;
  optionalDependencies?: Record<string, string>;
  peerDependencies?: Record<string, string>;
};

function shipped(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) return entry.name === '__snapshots__' ? [] : shipped(full);
    return /\.tsx?$/.test(entry.name) && !/\.test\.tsx?$/.test(entry.name) && entry.name !== 'fixtures.ts'
      ? [full]
      : [];
  });
}

// Every module a file loads: `import … from '…'`, `export … from '…'`, a side-effect `import '…'`, and a dynamic
// `import('…')`.
function specifiers(text: string): string[] {
  const found: string[] = [];
  for (const match of text.matchAll(/(?:^|\n)\s*(?:import|export)\b[^'"`]*?from\s+['"]([^'"]+)['"]/g)) found.push(match[1]);
  for (const match of text.matchAll(/(?:^|\n)\s*import\s*['"]([^'"]+)['"]/g)) found.push(match[1]);
  for (const match of text.matchAll(/\bimport\s*\(\s*['"`]([^'"`]+)['"`]\s*\)/g)) found.push(match[1]);
  return found;
}

// The package a bare specifier names: 'react/jsx-runtime' is 'react', '@scope/name/sub' is '@scope/name'.
function packageOf(specifier: string): string {
  const parts = specifier.split('/');
  return specifier.startsWith('@') ? parts.slice(0, 2).join('/') : parts[0];
}

describe('the package', () => {
  it('has no runtime dependencies', () => {
    expect(manifest.dependencies ?? {}).toEqual({});
    expect(manifest.optionalDependencies ?? {}).toEqual({});
  });

  it('imports nothing but its peers', () => {
    const peers = new Set(Object.keys(manifest.peerDependencies ?? {}));
    const strays: string[] = [];
    for (const file of shipped(here)) {
      for (const specifier of specifiers(readFileSync(file, 'utf8'))) {
        if (specifier.startsWith('.') || specifier.startsWith('node:')) continue;
        if (!peers.has(packageOf(specifier))) strays.push(`${path.relative(here, file)}: ${specifier}`);
      }
    }
    expect(strays).toEqual([]);
  });

  it('reads side-effect and dynamic imports', () => {
    const text = [
      "import 'vitest';",
      "import type { A } from '@testing-library/react';",
      "const lazy = await import('jsdom');",
      "export * from './local';",
    ].join('\n');
    expect(specifiers(text).sort()).toEqual(['./local', '@testing-library/react', 'jsdom', 'vitest']);
  });
});
