// The npm half of release.yml: the release job packs @traxsharp/state-machine at the released
// version and records its digest; the publish job checks the digest, attests the tarball and
// publishes it with npm trusted publishing, skipping a version npm already has. The shell steps
// are run here as written in the workflow, against fake folders and a fake curl and npm.
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import yaml from 'js-yaml';

const workflowPath = join(dirname(fileURLToPath(import.meta.url)), '..', 'workflows', 'release.yml');
const workflowText = readFileSync(workflowPath, 'utf8');
const workflow = yaml.load(workflowText);
const release = workflow.jobs.release;
const publish = workflow.jobs.publish;

const step = (job, name) => {
  const found = job.steps.find((s) => s.name === name);
  assert.ok(found, `no step named '${name}'`);
  return found;
};
const indexOf = (job, name) => job.steps.findIndex((s) => s.name === name);

// sha256sum's output for every file in `folder`, base64-encoded, as the release job records it.
const digestsOf = (folder) =>
  Buffer.from(execFileSync('sh', ['-c', 'sha256sum -- *', '--'], { cwd: folder })).toString('base64');

// Runs a step's script with bash, from `cwd`, as Actions would (bash -e).
const runStep = (script, cwd, env) =>
  spawnSync('bash', ['-e', '-c', script], { cwd, env: { ...process.env, ...env }, encoding: 'utf8' });

const sandbox = () => {
  const root = mkdtempSync(join(tmpdir(), 'trax-release-'));
  mkdirSync(join(root, 'nupkg'));
  mkdirSync(join(root, 'npm'));
  mkdirSync(join(root, 'tmp'));
  writeFileSync(join(root, 'nupkg', 'Trax.Core.1.63.0.nupkg'), 'core');
  writeFileSync(join(root, 'npm', 'traxsharp-state-machine-1.63.0.tgz'), 'engine');
  return root;
};

describe('release job', () => {
  it('packs the engine at the released version, only when a version was cut', () => {
    const pack = step(release, 'Pack the TypeScript engine');

    assert.equal(pack.if, "steps.version.outputs.released == 'true'");
    assert.equal(pack['working-directory'], 'Trax.Api.StateMachine');
    assert.equal(pack.env.PKG_VERSION, '${{ steps.version.outputs.version }}');
    assert.match(pack.run, /npm ci --ignore-scripts/);
    assert.match(pack.run, /npm test/);
    assert.match(pack.run, /npm version "\$PKG_VERSION" --no-git-tag-version/);
    assert.match(pack.run, /npm pack --pack-destination \.\.\/npm/);
    assert.ok(indexOf(release, 'Resolve released version') < indexOf(release, 'Pack the TypeScript engine'));
  });

  it('installs the engine before the NuGet packs, which bundle it into Trax.Cli', () => {
    assert.ok(indexOf(release, 'Pack the TypeScript engine') < indexOf(release, 'Pack every package'));
  });

  it('hands publish the tarball and its digest', () => {
    assert.equal(release.outputs['npm-digests'], '${{ steps.npm-digests.outputs.digests }}');
    assert.match(step(release, 'Record npm package digest').run, /sha256sum -- \*\.tgz/);
    assert.equal(step(release, 'Upload npm package artifact').with.name, 'npm');
  });
});

describe('publish job', () => {
  it('holds no npm token: it publishes through OIDC', () => {
    assert.equal(publish.permissions['id-token'], 'write');
    // The parsed workflow, so the comments that say there is no token do not count.
    assert.doesNotMatch(JSON.stringify(workflow), /NPM_TOKEN|NODE_AUTH_TOKEN|secrets\.NPM/);
  });

  it('verifies, then attests, then publishes the tarball', () => {
    const verify = indexOf(publish, 'Verify the packages are the ones release built');
    const attest = indexOf(publish, 'Attest build provenance (SLSA)');
    const npmPublish = indexOf(publish, 'Publish to npm');

    assert.ok(indexOf(publish, 'Download npm package artifact') < verify);
    assert.ok(verify < attest && attest < npmPublish);
    assert.match(publish.steps[attest].with['subject-path'], /^npm\/\*\.tgz$/m);
  });

  describe('the digest check', () => {
    const verify = step(publish, 'Verify the packages are the ones release built');

    const check = (root, tamper) => {
      const env = {
        RUNNER_TEMP: join(root, 'tmp'),
        DIGESTS: digestsOf(join(root, 'nupkg')),
        NPM_DIGESTS: digestsOf(join(root, 'npm')),
      };
      tamper(root);
      return runStep(verify.run, root, env);
    };

    it('passes the tarball the release job built', () => {
      const result = check(sandbox(), () => {});
      assert.equal(result.status, 0, result.stdout + result.stderr);
    });

    it('refuses a tarball whose digest differs', () => {
      const result = check(sandbox(), (root) =>
        writeFileSync(join(root, 'npm', 'traxsharp-state-machine-1.63.0.tgz'), 'tampered'),
      );
      assert.notEqual(result.status, 0);
    });

    it('refuses a tarball the release job did not build', () => {
      const result = check(sandbox(), (root) =>
        writeFileSync(join(root, 'npm', 'traxsharp-state-machine-6.6.6.tgz'), 'extra'),
      );
      assert.notEqual(result.status, 0);
      assert.match(result.stdout, /not the set the release job built/);
    });
  });

  describe('the npm publish', () => {
    const publishStep = step(publish, 'Publish to npm');

    // A fake curl answering `status`, and a fake npm that records its arguments.
    const run = (status) => {
      const root = sandbox();
      const bin = join(root, 'bin');
      mkdirSync(bin);
      writeFileSync(join(bin, 'curl'), `#!/bin/sh\nprintf '%s' '${status}'\n`);
      writeFileSync(join(bin, 'npm'), `#!/bin/sh\necho "$@" >> "${join(root, 'npm-calls')}"\n`);
      chmodSync(join(bin, 'curl'), 0o755);
      chmodSync(join(bin, 'npm'), 0o755);
      const result = runStep(publishStep.run, root, {
        PATH: `${bin}:${process.env.PATH}`,
        PKG_VERSION: '1.63.0',
      });
      let calls = '';
      try {
        calls = readFileSync(join(root, 'npm-calls'), 'utf8');
      } catch {
        // npm was never called.
      }
      return { result, calls };
    };

    it('publishes the packed tarball with provenance when npm does not have the version', () => {
      const { result, calls } = run('404');
      assert.equal(result.status, 0, result.stdout + result.stderr);
      assert.equal(
        calls.trim(),
        'publish npm/traxsharp-state-machine-1.63.0.tgz --provenance --access public --ignore-scripts',
      );
    });

    it('skips a version npm already has, so a re-run is safe', () => {
      const { result, calls } = run('200');
      assert.equal(result.status, 0, result.stdout + result.stderr);
      assert.equal(calls, '');
    });

    it('stops when the registry answers anything else', () => {
      const { result, calls } = run('503');
      assert.notEqual(result.status, 0);
      assert.equal(calls, '');
    });
  });
});
