// The deploy proof scripts restore trax and the sample from their lockfiles before anything else, and
// stop when a lockfile has drifted. Run on a copy, with every tool they call faked, so nothing on
// this machine is deployed to or cleaned up.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, copyFileSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const projects = [
  'Trax.Cli/src/Trax.Cli/Trax.Cli.csproj',
  'Trax.Samples/samples/DistributedWorkers/Trax.Samples.EnergyHub.Hub/Trax.Samples.EnergyHub.Hub.csproj',
  'Trax.Samples/samples/DistributedWorkers/Trax.Samples.EnergyHub.Worker/Trax.Samples.EnergyHub.Worker.csproj',
];

// Runs `script` with a dotnet whose locked restore fails (a drifted lockfile), and returns its
// exit status and every dotnet call it made.
const run = (script) => {
  const root = mkdtempSync(join(tmpdir(), 'trax-deploy-proof-'));
  const bin = join(root, 'bin');
  mkdirSync(bin);
  mkdirSync(join(root, '.github', 'ci', 'deploy'), { recursive: true });
  copyFileSync(join(here, script), join(root, '.github', 'ci', 'deploy', script));
  const calls = join(root, 'dotnet-calls');
  writeFileSync(join(bin, 'dotnet'), `#!/bin/sh\necho "$*" >> '${calls}'\ncase "$*" in *--locked-mode*) exit 1 ;; esac\n`);
  for (const tool of ['kubectl', 'docker', 'sudo', 'podman', 'systemctl', 'journalctl', 'curl'])
    writeFileSync(join(bin, tool), `#!/bin/sh\necho "${tool} $*" >> '${join(root, 'other-calls')}'\nexit 1\n`);
  for (const tool of ['dotnet', 'kubectl', 'docker', 'sudo', 'podman', 'systemctl', 'journalctl', 'curl'])
    chmodSync(join(bin, tool), 0o755);
  const result = spawnSync('bash', [join('.github', 'ci', 'deploy', script)], {
    cwd: root,
    env: {
      ...process.env,
      PATH: `${bin}:${process.env.PATH}`,
      HOME: root,
      XDG_CONFIG_HOME: join(root, 'config'),
      XDG_RUNTIME_DIR: root,
      RUNNER_TEMP: root,
      K8S: 'existing',
    },
    encoding: 'utf8',
  });
  let made = [];
  try {
    made = readFileSync(calls, 'utf8').trim().split('\n');
  } catch {
    // dotnet was never called.
  }
  return { result, calls: made };
};

for (const script of ['cluster.sh', 'podman.sh'])
  describe(script, () => {
    it('restores trax and the sample locked first, and stops when a lockfile has drifted', () => {
      const { result, calls } = run(script);
      assert.notEqual(result.status, 0);
      assert.ok(calls.length > 0, 'dotnet was never called');
      assert.match(calls[0], new RegExp(`^restore ${projects[0]} --locked-mode`));
      assert.ok(!calls.some((c) => c.startsWith('run ')), `trax ran on a drifted lockfile:\n${calls.join('\n')}`);
    });

    it('restores every project it builds', () => {
      const text = readFileSync(join(here, script), 'utf8');
      for (const project of projects) assert.ok(text.includes(project.replace('Trax.Samples/samples/DistributedWorkers', '$DIR')), project);
    });
  });
