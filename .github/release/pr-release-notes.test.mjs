import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { collectPullRequestNotes, composeNotes, extractReleaseNotes } from './pr-release-notes.mjs';

describe('extractReleaseNotes', () => {
  it('takes the section up to the next heading of the same level', () => {
    const body = [
      '## Background',
      'Why.',
      '## Release notes',
      'Trains can run branches side by side.',
      '### Parallel',
      'Details.',
      '## How this solves it',
      'Not this.',
    ].join('\r\n');

    assert.equal(extractReleaseNotes(body), 'Trains can run branches side by side.\n### Parallel\nDetails.');
  });

  it('moves sub-headings under a level-2 heading whatever level the section is', () => {
    assert.equal(extractReleaseNotes('# Release notes\n## Parallel\nText.'), '### Parallel\nText.');
    assert.equal(extractReleaseNotes('### Release notes\n#### Parallel\nText.'), '### Parallel\nText.');
  });

  it('ignores headings and the end of the section inside code fences', () => {
    const body = '## Release notes\n```bash\n## not a heading\n```\nAfter.\n## Next';

    assert.equal(extractReleaseNotes(body), '```bash\n## not a heading\n```\nAfter.');
  });

  it('is null without the section, or when only comments are in it', () => {
    assert.equal(extractReleaseNotes(null), null);
    assert.equal(extractReleaseNotes('## Background\nText.'), null);
    assert.equal(extractReleaseNotes('## Release notes\n<!-- what users will notice -->\n## Next'), null);
  });
});

describe('composeNotes', () => {
  const commitNotes = '# [1.62.0](compare) (2026-10-09)\n\n\n### Features\n\n* ms1 composition (#32)\n';

  it('puts each pull request between the version heading and the commit list', () => {
    const notes = composeNotes(commitNotes, [
      { number: 32, title: 'feat(core): ms1 composition', url: 'https://pr/32', notes: 'Parallel steps.' },
    ]);

    assert.equal(
      notes,
      '# [1.62.0](compare) (2026-10-09)\n\n' +
        '## Ms1 composition ([#32](https://pr/32))\n\nParallel steps.\n\n' +
        '## All changes\n\n### Features\n\n* ms1 composition (#32)\n',
    );
  });

  it('leaves the commit notes alone when no pull request has release notes', () => {
    assert.equal(composeNotes(commitNotes, []), commitNotes);
  });
});

describe('collectPullRequestNotes', () => {
  const pull = (number, mergedAt, body, extra = {}) => ({
    number,
    title: `feat: pr ${number}`,
    html_url: `https://pr/${number}`,
    merged_at: mergedAt,
    base: { ref: 'main' },
    user: { login: 'maintainer' },
    body,
    ...extra,
  });

  function fakeGitHub(pullsByCommit, permissions = {}) {
    const calls = [];
    const request = async (path) => {
      calls.push(path);
      const commit = /\/commits\/(\w+)\/pulls$/.exec(path);
      if (commit) return pullsByCommit[commit[1]] ?? [];
      const user = /\/collaborators\/([^/]+)\/permission$/.exec(path);
      if (user) return { permission: permissions[user[1]] ?? 'write' };
      throw new Error(`unexpected ${path}`);
    };
    return { request, calls };
  }

  const collect = (commits, github, warnings = []) =>
    collectPullRequestNotes({
      commits,
      branch: 'main',
      owner: 'TraxSharp',
      repo: 'Trax',
      request: github.request,
      logger: { warn: (message) => warnings.push(message) },
    });

  it('returns pull requests with notes in merge order, each once', async () => {
    const early = pull(1, '2026-10-01T00:00:00Z', '## Release notes\nFirst.');
    const late = pull(2, '2026-10-02T00:00:00Z', '## Release notes\nSecond.');
    const github = fakeGitHub({ a: [late], b: [late], c: [early] });

    const sections = await collect([{ hash: 'a' }, { hash: 'b' }, { hash: 'c' }], github);

    assert.deepEqual(
      sections.map((s) => [s.number, s.notes]),
      [
        [1, 'First.'],
        [2, 'Second.'],
      ],
    );
  });

  it('skips pull requests without notes, unmerged, or merged into another branch', async () => {
    const github = fakeGitHub({
      a: [
        pull(1, '2026-10-01T00:00:00Z', '## Background\nNo notes.'),
        pull(2, null, '## Release notes\nNot merged.'),
        pull(3, '2026-10-01T00:00:00Z', '## Release notes\nElsewhere.', { base: { ref: 'feat/x' } }),
      ],
    });

    assert.deepEqual(await collect([{ hash: 'a' }], github), []);
  });

  it('leaves out notes whose author cannot write to the repository, and says so', async () => {
    const outside = pull(4, '2026-10-01T00:00:00Z', '## Release notes\nTrust me.', { user: { login: 'outsider' } });
    const github = fakeGitHub({ a: [outside] }, { outsider: 'read' });
    const warnings = [];

    assert.deepEqual(await collect([{ hash: 'a' }], github, warnings), []);
    assert.match(warnings[0], /#4/);
  });

  it('fails when GitHub does, rather than publishing notes without the pull requests', async () => {
    const request = async () => {
      throw new Error('GitHub answered 502');
    };

    await assert.rejects(collect([{ hash: 'a' }], { request }), /502/);
  });
});
