import path from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';
import { describe, expect, it } from 'vitest';

// The published surface of each entry point, values and types alike, as a committed snapshot. A new export, or one
// removed, changes the snapshot, so it is a deliberate, reviewed change rather than a helper that leaked out of a
// module. Update it with `npx vitest run -u` and say why in the commit.

const here = path.dirname(fileURLToPath(import.meta.url));

const entryPoints = {
  '@traxsharp/state-machine': path.join(here, 'index.ts'),
  '@traxsharp/state-machine/client': path.join(here, 'client', 'index.ts'),
  '@traxsharp/state-machine/react': path.join(here, 'react.ts'),
};

function exportNames(): Record<string, string[]> {
  const config = ts.readConfigFile(path.join(here, '..', 'tsconfig.json'), ts.sys.readFile);
  const options = ts.parseJsonConfigFileContent(config.config, ts.sys, path.join(here, '..')).options;
  const program = ts.createProgram(Object.values(entryPoints), options);
  const checker = program.getTypeChecker();
  const result: Record<string, string[]> = {};
  for (const [name, file] of Object.entries(entryPoints)) {
    const source = program.getSourceFile(file);
    const symbol = source && checker.getSymbolAtLocation(source);
    if (!symbol) throw new Error(`${name}: ${file} is not a module`);
    result[name] = checker
      .getExportsOfModule(symbol)
      .map((s) => s.getName())
      .sort();
  }
  return result;
}

describe('the published exports', () => {
  it('match the committed baseline', () => {
    expect(exportNames()).toMatchSnapshot();
  }, 30_000);
});

// What package.json says each entry point resolves to. tsup writes an `.d.ts` beside each ESM file and an `.d.cts`
// beside each CommonJS one; in a `"type": "module"` package a `require` that resolved the `.d.ts` would be typed as
// ESM. The peers are bounded to the majors the package is tested on.
describe('the package manifest', () => {
  const manifest = JSON.parse(ts.sys.readFile(path.join(here, '..', 'package.json'))!) as {
    type: string;
    exports: Record<string, string | Record<string, Record<string, string>>>;
    peerDependencies: Record<string, string>;
  };
  const subpaths: Record<string, string> = { '.': 'index', './react': 'react', './client': 'client/index' };

  it.each(Object.entries(subpaths))('types %s for import and for require each with its own declarations', (subpath, file) => {
    expect(manifest.type).toBe('module');
    expect(manifest.exports[subpath]).toEqual({
      import: { types: `./dist/${file}.d.ts`, default: `./dist/${file}.js` },
      require: { types: `./dist/${file}.d.cts`, default: `./dist/${file}.cjs` },
    });
  });

  it('exports package.json, and every entry point tsup builds', () => {
    expect(manifest.exports['./package.json']).toBe('./package.json');
    expect(Object.keys(manifest.exports).sort()).toEqual([...Object.keys(subpaths), './package.json'].sort());
  });

  it('bounds each peer to the majors it supports', () => {
    expect(manifest.peerDependencies).toEqual({ 'graphql-ws': '^6.0.0', react: '^18.0.0 || ^19.0.0' });
  });
});
