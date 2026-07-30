import { execFileSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

/**
 * The single structural-drift guard: it runs the generator in --check mode, which fails if any committed
 * generated file is stale or a structure.json is no longer derivable from its machine.json. This replaces
 * per-machine structure-parity tests — once both engines read generated edges from one spec, structural
 * agreement is true by construction.
 */
describe('codegen drift', () => {
  it('every committed generated file is up to date (generate.mjs --check)', () => {
    const run = () =>
      execFileSync('node', ['tools/state-machine-codegen/generate.mjs', '--check'], {
        cwd: repoRoot,
        encoding: 'utf8',
      });
    expect(run).not.toThrow();
  });
});
