import path from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';
import { describe, expect, it } from 'vitest';

// The published surface of each entry point, values and types alike, as a committed snapshot. A new export, or one
// removed, changes the snapshot, so it is a deliberate, reviewed change rather than a helper that leaked out of a
// module. Update it with `npx vitest run -u` and say why in the commit.

const here = path.dirname(fileURLToPath(import.meta.url));

const entryPoints = {
  '@trax/state-machine': path.join(here, 'index.ts'),
  '@trax/state-machine/client': path.join(here, 'client', 'index.ts'),
  '@trax/state-machine/react': path.join(here, 'react.ts'),
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
