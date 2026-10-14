// release.yml's split: the release job only tags; the build job packs every package, and
// @traxsharp/state-machine, at the released version with a read-only token and records their
// digests; the publish job checks the digests, attests and publishes with trusted publishing,
// npm before NuGet: it stages the engine and waits for a maintainer to approve it, accepts a live
// version only when it is the tarball in hand, and installs nothing. The shell steps are run here as
// written in the workflow, against fake folders and a fake curl and npm.
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
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
const build = workflow.jobs.build;
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
  it('holds the write token and runs only the release tooling: it builds and packs nothing', () => {
    assert.equal(release.permissions.contents, 'write');
    const runs = release.steps.filter((s) => s.run).map((s) => s.run.trim());
    assert.deepEqual(runs.slice(0, 2), ['npm ci --ignore-scripts', 'npx semantic-release']);
    for (const run of runs) assert.doesNotMatch(run, /dotnet|npm (pack|test|run)|working-directory/);
    assert.ok(release.steps.every((s) => !s['working-directory']));
    assert.ok(release.steps.every((s) => !(s.uses ?? '').startsWith('actions/upload-artifact')));
  });

  it('runs only after the full CI passed at the same commit', () => {
    assert.equal(release.needs, 'build-test');
    assert.equal(workflow.jobs['build-test'].uses, './.github/workflows/ci.yml');
  });
});

describe('build job', () => {
  it('packs with a read-only token, outside the release environment', () => {
    assert.deepEqual(build.permissions, { contents: 'read' });
    assert.equal(build.environment, undefined);
    assert.equal(build.needs, 'release');
    assert.equal(build.if, "needs.release.outputs.released == 'true'");
  });

  it('packs the tag, and refuses one that is not the commit CI validated', () => {
    const checkout = step(build, 'Checkout the tag');
    assert.equal(checkout.with.ref, 'refs/tags/v${{ needs.release.outputs.version }}');
    assert.equal(checkout.with['persist-credentials'], false);
    assert.match(step(build, 'The tag is the commit build-test validated').run, /"\$GITHUB_SHA"/);
  });

  it('packs the engine at the released version, without re-running the tests CI ran', () => {
    const pack = step(build, 'Pack the TypeScript engine');

    assert.equal(pack['working-directory'], 'Trax.Api.StateMachine');
    assert.equal(pack.env.PKG_VERSION, '${{ needs.release.outputs.version }}');
    assert.match(pack.run, /npm ci --ignore-scripts/);
    assert.doesNotMatch(pack.run, /npm test|npm run typecheck/);
    assert.match(pack.run, /npm version "\$PKG_VERSION" --no-git-tag-version/);
    assert.match(pack.run, /npm pack --pack-destination \.\.\/npm/);
  });

  it('versions the engine before the NuGet packs, which bundle it into Trax.Cli', () => {
    assert.ok(indexOf(build, 'Pack the TypeScript engine') < indexOf(build, 'Pack every package'));
  });

  it('installs the API dashboard before the NuGet packs, which build it into Trax.Api.Dashboard', () => {
    const install = step(build, 'Install the API dashboard (locked, no install scripts)');
    assert.equal(install['working-directory'], 'Trax.Api/src/Trax.Api.Dashboard/ClientApp');
    assert.equal(install.run, 'npm ci --ignore-scripts');
    assert.ok(indexOf(build, 'Install the API dashboard (locked, no install scripts)') < indexOf(build, 'Pack every package'));
  });

  it('hands publish the packages and their digests', () => {
    assert.equal(build.outputs.digests, '${{ steps.digests.outputs.digests }}');
    assert.equal(build.outputs['npm-digests'], '${{ steps.npm-digests.outputs.digests }}');
    assert.match(step(build, 'Record npm package digest').run, /sha256sum -- \*\.tgz/);
    assert.equal(step(build, 'Upload npm package artifact').with.name, 'npm');
    assert.equal(step(build, 'Upload package artifact').with.name, 'nupkg');
  });
});

describe('publish job', () => {
  it('holds no npm token: it publishes through OIDC', () => {
    assert.equal(publish.permissions['id-token'], 'write');
    // The parsed workflow, so the comments that say there is no token do not count.
    assert.doesNotMatch(JSON.stringify(workflow), /NPM_TOKEN|NODE_AUTH_TOKEN|secrets\.NPM/);
  });

  it('installs nothing: the npm that publishes is the one bundled with an exact Node.js', () => {
    for (const s of publish.steps.filter((s) => s.run))
      assert.doesNotMatch(s.run, /npm (install|ci|i) |npx /);
    assert.match(workflow.env.PUBLISH_NODE_VERSION, /^\d+\.\d+\.\d+$/);
    assert.equal(step(publish, 'Setup Node.js').with['node-version'], '${{ env.PUBLISH_NODE_VERSION }}');
  });

  it('checks the bundled npm can stage with OIDC (11.15.0 or later)', () => {
    const check = step(publish, 'The bundled npm can stage with OIDC').run;
    const run = (version) => {
      const root = mkdtempSync(join(tmpdir(), 'trax-npm-version-'));
      writeFileSync(join(root, 'npm'), `#!/bin/sh\necho ${version}\n`);
      chmodSync(join(root, 'npm'), 0o755);
      return runStep(check, root, { PATH: `${root}:${process.env.PATH}` }).status;
    };
    assert.equal(run('11.19.0'), 0);
    assert.equal(run('11.15.0'), 0);
    assert.equal(run('12.0.0'), 0);
    assert.notEqual(run('11.14.9'), 0);
    assert.notEqual(run('11.5.1'), 0);
    assert.notEqual(run('10.9.2'), 0);
  });

  it('takes its digests from the build job', () => {
    const verify = step(publish, 'Verify the packages are the ones build built');
    assert.deepEqual(publish.needs, ['release', 'build']);
    assert.equal(verify.env.DIGESTS, '${{ needs.build.outputs.digests }}');
    assert.equal(verify.env.NPM_DIGESTS, '${{ needs.build.outputs.npm-digests }}');
  });

  it('verifies, then attests, then stages the tarball', () => {
    const verify = indexOf(publish, 'Verify the packages are the ones build built');
    const attest = indexOf(publish, 'Attest build provenance (SLSA)');
    const npmPublish = indexOf(publish, 'Stage on npm and wait for approval');

    assert.ok(indexOf(publish, 'Download npm package artifact') < verify);
    assert.ok(verify < attest && attest < npmPublish);
    assert.match(publish.steps[attest].with['subject-path'], /^npm\/\*\.tgz$/m);
  });

  it('publishes to npm before NuGet, so no NuGet package ships unless npm holds our engine', () => {
    const npmPublish = indexOf(publish, 'Stage on npm and wait for approval');
    assert.ok(npmPublish >= 0);
    assert.ok(indexOf(publish, 'Setup Node.js') < npmPublish);
    assert.ok(indexOf(publish, 'The bundled npm can stage with OIDC') < npmPublish);
    assert.ok(npmPublish < indexOf(publish, 'NuGet login (OIDC -> short-lived key)'));
    assert.ok(npmPublish < indexOf(publish, 'Push to NuGet'));
  });

  describe('the digest check', () => {
    const verify = step(publish, 'Verify the packages are the ones build built');

    const check = (root, tamper) => {
      const env = {
        RUNNER_TEMP: join(root, 'tmp'),
        DIGESTS: digestsOf(join(root, 'nupkg')),
        NPM_DIGESTS: digestsOf(join(root, 'npm')),
      };
      tamper(root);
      return runStep(verify.run, root, env);
    };

    it('passes the tarball the build job built', () => {
      const result = check(sandbox(), () => {});
      assert.equal(result.status, 0, result.stdout + result.stderr);
    });

    it('refuses a tarball whose digest differs', () => {
      const result = check(sandbox(), (root) =>
        writeFileSync(join(root, 'npm', 'traxsharp-state-machine-1.63.0.tgz'), 'tampered'),
      );
      assert.notEqual(result.status, 0);
    });

    it('refuses a tarball the build job did not build', () => {
      const result = check(sandbox(), (root) =>
        writeFileSync(join(root, 'npm', 'traxsharp-state-machine-6.6.6.tgz'), 'extra'),
      );
      assert.notEqual(result.status, 0);
      assert.match(result.stdout, /not the set the build job built/);
    });
  });

  describe('the npm stage', () => {
    const stageStep = step(publish, 'Stage on npm and wait for approval');
    const tarball = 'engine'; // the sandbox's tarball bytes
    const ours = {
      integrity: `sha512-${createHash('sha512').update(tarball).digest('base64')}`,
      shasum: createHash('sha1').update(tarball).digest('hex'),
    };
    const manifest = (dist) => JSON.stringify({ name: '@traxsharp/state-machine', version: '1.63.0', dist });

    // A fake curl giving each call the next of `answers` ([status, body]; the last repeats), and a
    // fake npm that records its arguments and exits with `npmExit`.
    const run = (answers, { npmExit = 0, seconds = '5' } = {}) => {
      const root = sandbox();
      const bin = join(root, 'bin');
      mkdirSync(bin);
      answers.forEach(([status, body = ''], i) => {
        writeFileSync(join(root, `status-${i}`), status);
        writeFileSync(join(root, `body-${i}`), body);
      });
      writeFileSync(
        join(bin, 'curl'),
        `#!/bin/sh
out=
while [ $# -gt 0 ]; do
  if [ "$1" = -o ]; then out="$2"; shift; fi
  shift
done
n=$(cat '${join(root, 'calls')}' 2>/dev/null || echo 0)
echo $((n + 1)) > '${join(root, 'calls')}'
i=$n; [ "$i" -gt ${answers.length - 1} ] && i=${answers.length - 1}
[ -n "$out" ] && cp "${root}/body-$i" "$out"
cat "${root}/status-$i"
`,
      );
      writeFileSync(join(bin, 'npm'), `#!/bin/sh\necho "$@" >> "${join(root, 'npm-calls')}"\nexit ${npmExit}\n`);
      chmodSync(join(bin, 'curl'), 0o755);
      chmodSync(join(bin, 'npm'), 0o755);
      const result = runStep(stageStep.run, root, {
        PATH: `${bin}:${process.env.PATH}`,
        PKG_VERSION: '1.63.0',
        RUNNER_TEMP: join(root, 'tmp'),
        NPM_APPROVAL_SECONDS: seconds,
        NPM_POLL_SECONDS: '0',
        GITHUB_STEP_SUMMARY: '',
      });
      let calls = '';
      try {
        calls = readFileSync(join(root, 'npm-calls'), 'utf8');
      } catch {
        // npm was never called.
      }
      return { result, calls };
    };
    const staged = 'stage publish npm/traxsharp-state-machine-1.63.0.tgz --provenance --access public --ignore-scripts';

    it('stages the packed tarball with provenance, and passes once the approved version is ours', () => {
      const { result, calls } = run([['404'], ['404'], ['404'], ['200', manifest(ours)]]);
      assert.equal(result.status, 0, result.stdout + result.stderr);
      assert.equal(calls.trim(), staged);
      assert.match(result.stdout, new RegExp(`staged shasum is ${ours.shasum}`));
      assert.match(result.stdout, /is the tarball this run built/);
    });

    it('never publishes directly or moves a dist-tag', () => {
      assert.doesNotMatch(stageStep.run, /npm publish|npm dist-tag/);
    });

    it('keeps waiting through a registry error while the version is staged', () => {
      const { result } = run([['404'], ['503'], ['200', manifest(ours)]]);
      assert.equal(result.status, 0, result.stdout + result.stderr);
    });

    it('fails when the approved version is not the tarball in hand', () => {
      const theirs = createHash('sha512').update('not ours').digest('base64');
      const { result, calls } = run([['404'], ['200', manifest({ ...ours, integrity: `sha512-${theirs}` })]]);
      assert.notEqual(result.status, 0);
      assert.match(result.stdout, /not the tarball this run built/);
      assert.equal(calls.trim(), staged);
    });

    it('fails, and leaves NuGet alone, when nobody approves in time', () => {
      const { result } = run([['404']], { seconds: '1' });
      assert.notEqual(result.status, 0);
      assert.match(result.stdout, /staged but not approved/);
    });

    it('fails with what to do when npm refuses to stage, as for a version an earlier run staged', () => {
      const { result } = run([['404']], { npmExit: 1 });
      assert.notEqual(result.status, 0);
      assert.match(result.stdout, /refused to stage.*approve it, and re-run/);
    });

    it('accepts a version already live when it is the tarball in hand, so a re-run is safe', () => {
      const { result, calls } = run([['200', manifest(ours)]]);
      assert.equal(result.status, 0, result.stdout + result.stderr);
      assert.match(result.stdout, /is the tarball this run built/);
      assert.equal(calls, '');
    });

    it('fails a version already live with other bytes, which someone else published', () => {
      const theirs = createHash('sha512').update('not ours').digest('base64');
      const { result, calls } = run([['200', manifest({ ...ours, integrity: `sha512-${theirs}` })]]);
      assert.notEqual(result.status, 0);
      assert.match(result.stdout, /not the tarball this run built/);
      assert.equal(calls, '');
    });

    it('fails when only the shasum differs', () => {
      const { result, calls } = run([['200', manifest({ ...ours, shasum: '0'.repeat(40) })]]);
      assert.notEqual(result.status, 0);
      assert.equal(calls, '');
    });

    it('fails when npm gives no digests for the version', () => {
      const { result, calls } = run([['200', manifest({})]]);
      assert.notEqual(result.status, 0);
      assert.equal(calls, '');
    });

    it('fails when the registry answers found with something that is not JSON', () => {
      const { result, calls } = run([['200', '<html>']]);
      assert.notEqual(result.status, 0);
      assert.equal(calls, '');
    });

    it('stops before staging when the registry answers anything else', () => {
      const { result, calls } = run([['503']]);
      assert.notEqual(result.status, 0);
      assert.equal(calls, '');
    });
  });
});
