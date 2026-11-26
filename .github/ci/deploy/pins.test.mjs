// The images the deploy proof pulls and the k3s it installs are pinned in a Dockerfile that
// Dependabot's docker ecosystem watches, and the scripts take their pins from it.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { describe, it } from 'node:test';
import { fileURLToPath } from 'node:url';
import yaml from 'js-yaml';

const here = dirname(fileURLToPath(import.meta.url));
const dockerfile = readFileSync(join(here, 'Dockerfile'), 'utf8');
const from = (image) => dockerfile.match(new RegExp(`^FROM (docker\\.io/${image}:\\S+) AS `, 'm'))?.[1];

describe('the deploy proof pins', () => {
  it('are watched by Dependabot', () => {
    const config = yaml.load(readFileSync(join(here, '..', '..', 'dependabot.yml'), 'utf8'));
    assert.ok(config.updates.some((u) => u['package-ecosystem'] === 'docker' && u.directory === '/.github/ci/deploy'));
  });

  it('pin each image by digest, and k3s by its release tag', () => {
    assert.match(from('library/registry'), /^docker\.io\/library\/registry:[\w.]+@sha256:[0-9a-f]{64}$/);
    assert.match(from('library/busybox'), /^docker\.io\/library\/busybox:[\w.]+@sha256:[0-9a-f]{64}$/);
    assert.match(from('rancher/k3s'), /^docker\.io\/rancher\/k3s:v\d+\.\d+\.\d+-k3s\d+$/);
  });

  it('are what cluster.sh uses', () => {
    const vars = execFileSync('bash', ['-c', `. '${join(here, 'pins.sh')}' && printf '%s\\n' "$REGISTRY_IMAGE" "$BUSYBOX" "$K3S_VERSION"`], {
      encoding: 'utf8',
    })
      .trim()
      .split('\n');
    assert.deepEqual(vars, [from('library/registry'), from('library/busybox'), from('rancher/k3s').split(':')[1].replace('-k3s', '+k3s')]);
    const script = readFileSync(join(here, 'cluster.sh'), 'utf8');
    assert.match(script, /^\. "\$\(dirname "\$0"\)\/pins\.sh"$/m);
    assert.doesNotMatch(script, /@sha256:[0-9a-f]{64}/);
    assert.doesNotMatch(script, /^K3S_VERSION=/m);
  });
});
