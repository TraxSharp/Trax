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
const pushEgress = workflow.jobs['push-egress'];

const step = (job, name) => {
  const found = job.steps.find((s) => s.name === name);
  assert.ok(found, `no step named '${name}'`);
  return found;
};
const indexOf = (job, name) => job.steps.findIndex((s) => s.name === name);
// The hosts a job's harden-runner step lets it reach.
const allowedEndpoints = (job) => step(job, 'Harden runner').with['allowed-endpoints'].split(/\s+/).filter(Boolean);

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

    assert.equal(pack['working-directory'], 'Trax.StateMachine/ts');
    assert.equal(pack.env.PKG_VERSION, '${{ needs.release.outputs.version }}');
    assert.match(pack.run, /npm ci --ignore-scripts/);
    assert.doesNotMatch(pack.run, /npm test|npm run typecheck/);
    assert.match(pack.run, /npm version "\$PKG_VERSION" --no-git-tag-version/);
    assert.match(pack.run, /npm pack --pack-destination \.\.\/\.\.\/npm/);
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

  it('packs every folder solution and every suite marked pack, the C# state-machine engine among them', () => {
    const run = step(build, 'Pack every package').run;
    const program = run.match(/jq -r '([\s\S]*?)' \\/)[1];
    const packages = join(dirname(workflowPath), '..', 'ci', 'packages.json');
    const list = execFileSync('jq', ['-r', program, packages], { encoding: 'utf8' }).trim().split('\n');

    assert.ok(list.includes('Trax.Effect/Trax.Effect.slnx'));
    assert.ok(list.includes('Trax.StateMachine/dotnet/Trax.StateMachine.slnx'));
    assert.equal(new Set(list).size, list.length);
  });

  it('may reach every Docker Hub host the egress image\'s base images come from, as CI may', () => {
    // Docker Hub redirects a layer to whichever of its blob hosts it likes; deploy-cluster in
    // ci.yml lists the ones it has been seen to use. The release build and CI's egress job pull
    // the same base images.
    const ci = yaml.load(readFileSync(join(dirname(workflowPath), 'ci.yml'), 'utf8'));
    const dockerHub = allowedEndpoints(ci.jobs['deploy-cluster']).filter((h) => /docker/.test(h));
    assert.ok(dockerHub.length >= 6, dockerHub.join(' '));
    for (const job of [build, ci.jobs.egress])
      for (const host of dockerHub) assert.ok(allowedEndpoints(job).includes(host), `${host} is not allowed`);
  });

  describe('the egress image', () => {
    const buildStep = step(build, 'Build the egress image');
    const index = (platforms) =>
      JSON.stringify({
        schemaVersion: 2,
        mediaType: 'application/vnd.oci.image.index.v1+json',
        manifests: platforms.map((p) => {
          const [os, architecture] = p.split('/');
          return { mediaType: 'application/vnd.oci.image.manifest.v1+json', digest: `sha256:${'c'.repeat(64)}`, size: 1, platform: { os, architecture } };
        }),
      });

    // A fake podman that logs its calls and writes the archive, and a fake skopeo that reads
    // `raw` back as the archive's top-level manifest.
    const run = (raw) => {
      const root = mkdtempSync(join(tmpdir(), 'trax-egress-build-'));
      const bin = join(root, 'bin');
      mkdirSync(bin);
      mkdirSync(join(root, 'tmp'));
      writeFileSync(join(root, 'raw'), raw);
      writeFileSync(
        join(bin, 'podman'),
        `#!/bin/sh
echo "podman $*" >> '${join(root, 'calls')}'
for a in "$@"; do case "$a" in oci-archive:*) printf archive > "\${a#oci-archive:}" ;; esac; done
`,
      );
      writeFileSync(join(bin, 'skopeo'), `#!/bin/sh\necho "skopeo $*" >> '${join(root, 'calls')}'\ncat '${join(root, 'raw')}'\n`);
      writeFileSync(join(bin, 'sudo'), `#!/bin/sh\necho "sudo $*" >> '${join(root, 'calls')}'\n`);
      for (const f of ['podman', 'skopeo', 'sudo']) chmodSync(join(bin, f), 0o755);
      const result = runStep(buildStep.run, root, {
        PATH: `${bin}:${process.env.PATH}`,
        PKG_VERSION: '1.63.0',
        RUNNER_TEMP: join(root, 'tmp'),
        GITHUB_OUTPUT: join(root, 'output'),
      });
      let output = '';
      try {
        output = readFileSync(join(root, 'output'), 'utf8');
      } catch {
        // The step failed before writing it.
      }
      return { result, output, calls: readFileSync(join(root, 'calls'), 'utf8') };
    };

    it('builds amd64 and arm64 into one index, and hands on the index digest', () => {
      const raw = index(['linux/amd64', 'linux/arm64']);
      const { result, output, calls } = run(raw);
      assert.equal(result.status, 0, result.stdout + result.stderr);
      assert.match(calls, /podman build .*--platform linux\/amd64,linux\/arm64 .*--manifest /);
      assert.match(calls, /podman manifest push --all --format oci .* oci-archive:egress\/trax-egress\.tar/);
      assert.match(output, new RegExp(`^digest=sha256:${createHash('sha256').update(raw).digest('hex')}$`, 'm'));
      assert.match(output, new RegExp(`^archive=${createHash('sha256').update('archive').digest('hex')}$`, 'm'));
    });

    it('refuses an index without arm64', () => {
      const { result } = run(index(['linux/amd64']));
      assert.notEqual(result.status, 0);
      assert.match(result.stdout, /not linux\/amd64 and linux\/arm64/);
    });

    it('refuses a single image where an index belongs', () => {
      const { result } = run(JSON.stringify({ schemaVersion: 2, mediaType: 'application/vnd.oci.image.manifest.v1+json' }));
      assert.notEqual(result.status, 0);
    });

    it('builds and runs on arm64 in CI too, so an arm64 break shows before a tag is cut', () => {
      const egress = yaml.load(readFileSync(join(dirname(workflowPath), 'ci.yml'), 'utf8')).jobs.egress;
      assert.ok(allowedEndpoints(egress).includes('azure.archive.ubuntu.com:80'));
      assert.match(step(egress, 'Install qemu for the arm64 build').run, /apt-get install .*qemu-user-binfmt/);
      assert.match(step(egress, 'Build the image for arm64, and run it').run, /--platform linux\/arm64/);
    });

    it('builds the egress image on the same runner image as CI, where podman resolves names behind harden-runner', () => {
      const egress = yaml.load(readFileSync(join(dirname(workflowPath), 'ci.yml'), 'utf8')).jobs.egress;
      assert.equal(build['runs-on'], 'ubuntu-latest');
      assert.equal(egress['runs-on'], build['runs-on']);
    });

    it('may reach the Ubuntu archive, for the qemu the arm64 build runs under', () => {
      assert.ok(allowedEndpoints(build).includes('azure.archive.ubuntu.com:80'));
      // A runner on AWS reaches its region's EC2 mirror, falling back to archive.ubuntu.com.
      for (const job of [build, yaml.load(readFileSync(join(dirname(workflowPath), 'ci.yml'), 'utf8')).jobs.egress])
        for (const host of ['*.ec2.archive.ubuntu.com:80', 'archive.ubuntu.com:80', 'security.ubuntu.com:80'])
          assert.ok(allowedEndpoints(job).includes(host), host);
      assert.match(step(build, 'Install qemu for the arm64 build').run, /apt-get install .*qemu-user-binfmt/);
      assert.ok(indexOf(build, 'Install qemu for the arm64 build') < indexOf(build, 'Build the egress image'));
    });
  });

  it('hands publish the packages and their digests', () => {
    assert.equal(build.outputs.digests, '${{ steps.digests.outputs.digests }}');
    assert.equal(build.outputs['npm-digests'], '${{ steps.npm-digests.outputs.digests }}');
    assert.match(step(build, 'Record npm package digest').run, /sha256sum -- \*\.tgz/);
    assert.equal(step(build, 'Upload npm package artifact').with.name, 'npm');
    assert.equal(step(build, 'Upload package artifact').with.name, 'nupkg');
  });
});

describe('push-egress job', () => {
  it('is the only job that may write packages, and it holds nothing else', () => {
    assert.deepEqual(pushEgress.permissions, { packages: 'write' });
    for (const [name, job] of Object.entries(workflow.jobs))
      if (name !== 'push-egress') assert.equal(job.permissions?.packages, undefined, `${name} may write packages`);
  });

  it('runs after the build, behind the release environment, and before publish', () => {
    assert.deepEqual(pushEgress.needs, ['release', 'build']);
    assert.equal(pushEgress.if, "needs.release.outputs.released == 'true'");
    assert.equal(pushEgress.environment, 'release');
    assert.ok(publish.needs.includes('push-egress'));
  });

  it('pushes only the archive the build job built', () => {
    const verify = step(pushEgress, 'Verify the egress image is the one build built');
    assert.equal(verify.env.EGRESS_ARCHIVE, '${{ needs.build.outputs.egress-archive }}');
    assert.ok(indexOf(pushEgress, 'Verify the egress image is the one build built') < indexOf(pushEgress, 'Push the egress image'));
  });
});

describe('the egress push', () => {
  const pushStep = step(pushEgress, 'Push the egress image');
  const raw = '{"mediaType":"application/vnd.oci.image.index.v1+json"}';
  const digest = `sha256:${createHash('sha256').update(raw).digest('hex')}`;

  // A fake skopeo: the archive's top-level manifest is `raw`; the registry answers an inspect with
  // `held` when the caller is logged in, or when the package is public. It logs every call.
  const run = ({ isPublic, held = raw }) => {
    const root = mkdtempSync(join(tmpdir(), 'trax-egress-push-'));
    const bin = join(root, 'bin');
    mkdirSync(bin);
    mkdirSync(join(root, 'tmp'));
    writeFileSync(join(root, 'raw'), raw);
    writeFileSync(join(root, 'held'), held);
    writeFileSync(
      join(bin, 'skopeo'),
      `#!/bin/sh
echo "$*" >> '${join(root, 'calls')}'
case "$1" in
  login) cat > /dev/null; touch '${join(root, 'session')}' ;;
  logout) rm -f '${join(root, 'session')}' ;;
  copy) ;;
  inspect)
    for a in "$@"; do last="$a"; done
    case "$last" in
      oci-archive:*) cat '${join(root, 'raw')}' ;;
      docker://*)
        anonymous=; for a in "$@"; do [ "$a" = --no-creds ] && anonymous=1; done
        if [ ${isPublic ? 1 : 0} = 1 ] || { [ -z "$anonymous" ] && [ -e '${join(root, 'session')}' ]; }; then
          cat '${join(root, 'held')}'
        else
          echo 'unauthorized' >&2; exit 1
        fi ;;
    esac ;;
esac
`,
    );
    chmodSync(join(bin, 'skopeo'), 0o755);
    const result = runStep(pushStep.run, root, {
      PATH: `${bin}:${process.env.PATH}`,
      PKG_VERSION: '1.63.0',
      EGRESS_DIGEST: digest,
      GHCR_USER: 'bot',
      GHCR_TOKEN: 'token',
      RUNNER_TEMP: join(root, 'tmp'),
    });
    return { result, calls: readFileSync(join(root, 'calls'), 'utf8').trim().split('\n') };
  };

  it('passes when anyone can pull the pushed digest', () => {
    const { result } = run({ isPublic: true });
    assert.equal(result.status, 0, result.stdout + result.stderr);
  });

  it('fails, before NuGet, when the package cannot be pulled without credentials', () => {
    const { result } = run({ isPublic: false });
    assert.notEqual(result.status, 0);
    assert.match(result.stdout, /cannot be pulled without credentials/);
  });

  it('reads the digest back only after logging out', () => {
    const { calls } = run({ isPublic: true });
    const logout = calls.findIndex((c) => c.startsWith('logout'));
    const readBack = calls.findIndex((c) => c.startsWith('inspect') && c.includes('docker://'));
    assert.ok(logout >= 0 && logout < readBack, calls.join('\n'));
  });

  it('fails when the registry holds another digest', () => {
    const { result } = run({ isPublic: true, held: '{"other":true}' });
    assert.notEqual(result.status, 0);
    assert.match(result.stdout, new RegExp(`not ${digest}`));
  });

  it('pushes every platform in the index, keeping its digest', () => {
    const { calls } = run({ isPublic: true });
    assert.ok(calls.some((c) => /^copy --all --preserve-digests oci-archive:/.test(c)), calls.join('\n'));
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
    assert.deepEqual(publish.needs, ['release', 'build', 'push-egress']);
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

  it('attests the egress image by the digest users pull, not the archive it came in', () => {
    const attest = step(publish, 'Attest the egress image (SLSA)');
    assert.equal(attest.uses, step(publish, 'Attest build provenance (SLSA)').uses);
    assert.equal(attest.with['subject-name'], 'ghcr.io/traxsharp/trax-egress');
    assert.equal(attest.with['subject-digest'], '${{ needs.build.outputs.egress-digest }}');
    assert.doesNotMatch(step(publish, 'Attest build provenance (SLSA)').with['subject-path'], /egress/);
    assert.ok(indexOf(publish, 'Attest the egress image (SLSA)') < indexOf(publish, 'Push to NuGet'));
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
    const staged = 'stage publish ./npm/traxsharp-state-machine-1.63.0.tgz --provenance --access public --ignore-scripts';

    it('stages the packed tarball with provenance, and passes once the approved version is ours', () => {
      const { result, calls } = run([['404'], ['404'], ['404'], ['200', manifest(ours)]]);
      assert.equal(result.status, 0, result.stdout + result.stderr);
      assert.equal(calls.trim(), staged);
      assert.match(result.stdout, new RegExp(`staged shasum is ${ours.shasum}`));
      assert.match(result.stdout, /is the tarball this run built/);
    });

    it('names the tarball as a local path, which npm would otherwise read as a GitHub repository', () => {
      const { calls } = run([['404'], ['200', manifest(ours)]]);
      const tarball = calls.trim().split(' ')[2];
      assert.match(tarball, /^(\.\/|\/)/, `npm reads ${tarball} as owner/repo on GitHub`);
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
