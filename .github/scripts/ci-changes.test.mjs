// ci-changes.sh against a fake git that reports a pull request's changed files: which suites run.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');

const suitesFor = (...changed) => {
  const temp = mkdtempSync(join(tmpdir(), 'trax-ci-changes-'));
  const bin = join(temp, 'bin');
  mkdirSync(bin);
  writeFileSync(join(temp, 'changed'), changed.join('\n') + '\n');
  writeFileSync(join(bin, 'git'), `#!/bin/sh\ncat '${join(temp, 'changed')}'\n`);
  chmodSync(join(bin, 'git'), 0o755);
  const result = spawnSync('bash', ['.github/scripts/ci-changes.sh', 'pull_request'], {
    cwd: root,
    env: { ...process.env, PATH: `${bin}:${process.env.PATH}`, GITHUB_OUTPUT: join(temp, 'output') },
    encoding: 'utf8',
  });
  assert.equal(result.status, 0, result.stdout + result.stderr);
  const line = readFileSync(join(temp, 'output'), 'utf8').split('\n').find((l) => l.startsWith('suite-names='));
  return JSON.parse(line.slice('suite-names='.length));
};

describe('deploy-cluster', () => {
  // The code its drain proof runs: the drain command and store, the drain table, the deploy
  // entry point, and the scheduler's side of a drain, its worker and its startup.
  for (const file of [
    'Trax.Effect/src/Trax.Effect.Data/Services/Drain/DrainStore.cs',
    'Trax.Effect/src/Trax.Effect.Data/Services/Drain/DrainCommand.cs',
    'Trax.Effect/src/Trax.Effect.Data/Models/Drain/PersistentDrain.cs',
    'Trax.Effect/src/Trax.Effect/Models/Drain/Drain.cs',
    'Trax.Effect/src/Trax.Effect.Data/Extensions/TraxDeployExtensions.cs',
    'Trax.Effect/src/Trax.Effect.Data.Postgres/Migrations/091_drain.sql',
    'Trax.Scheduler/src/Trax.Scheduler/Services/Drain/SchedulerDrain.cs',
    'Trax.Scheduler/src/Trax.Scheduler/Services/Operations/DrainBusyWork.cs',
    'Trax.Scheduler/src/Trax.Scheduler/Services/LocalWorkerService/LocalWorkerService.cs',
    'Trax.Scheduler/src/Trax.Scheduler/Services/SchedulerStartupService/SchedulerStartupService.cs',
  ])
    it(`runs when ${file.split('/').pop()} changes`, () => {
      assert.ok(suitesFor(file).includes('deploy-cluster'));
    });

  it('does not run for a change it does not prove', () => {
    assert.ok(!suitesFor('Trax.Effect/src/Trax.Effect/Extensions/EnumerableExtensions.cs').includes('deploy-cluster'));
  });
});
