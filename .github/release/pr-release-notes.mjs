// semantic-release plugin: the commit notes, with each merged pull request's own release notes
// above them.
//
// The stock notes list one line per commit subject, so a squash-merged pull request reads as a
// single line however much it changed. A pull request whose description has a `## Release notes`
// section (any heading level) gets that section copied into the release, under a heading named
// after its title, in the order the pull requests merged. The commit list follows under
// "All changes". A pull request without the section adds nothing, so small fixes stay one line.
//
// Only a section written by someone with write access to the repository is copied: the notes are
// published without review once the release is approved, so an outside contributor's description
// is never published as Trax's own words.
//
// .releaserc.json names this file in place of @semantic-release/release-notes-generator, and the
// options it is given pass through to that generator.
import { generateNotes as generateCommitNotes } from '@semantic-release/release-notes-generator';

const WRITE_PERMISSIONS = new Set(['admin', 'maintain', 'write']);

export async function generateNotes(pluginConfig, context) {
  const commitNotes = await generateCommitNotes(pluginConfig, context);
  const { owner, repo } = repositoryOf(context);
  const sections = await collectPullRequestNotes({
    commits: context.commits,
    branch: context.branch.name,
    owner,
    repo,
    request: gitHubRequest(context.env),
    logger: context.logger,
  });
  return composeNotes(commitNotes, sections);
}

// The text under a "Release notes" heading, up to the next heading of the same or a higher level,
// with its own sub-headings moved to sit under a level-2 heading. Null when there is none or it
// is empty once HTML comments are removed.
export function extractReleaseNotes(body) {
  if (!body) return null;
  const lines = body.replace(/\r\n?/g, '\n').split('\n');
  let level = 0;
  let start = -1;
  let end = lines.length;
  let fenced = false;
  for (let i = 0; i < lines.length; i++) {
    if (/^\s*(```|~~~)/.test(lines[i])) fenced = !fenced;
    if (fenced) continue;
    const heading = /^(#{1,6})\s+(.*?)\s*#*\s*$/.exec(lines[i]);
    if (!heading) continue;
    if (start < 0) {
      if (/^release notes$/i.test(heading[2])) {
        level = heading[1].length;
        start = i + 1;
      }
    } else if (heading[1].length <= level) {
      end = i;
      break;
    }
  }
  if (start < 0) return null;

  const section = lines
    .slice(start, end)
    .join('\n')
    .replace(/<!--[\s\S]*?-->/g, '')
    .trim();
  if (!section) return null;
  return shiftHeadings(section, 3 - (level + 1));
}

function shiftHeadings(text, by) {
  let fenced = false;
  return text
    .split('\n')
    .map((line) => {
      if (/^\s*(```|~~~)/.test(line)) fenced = !fenced;
      if (fenced || by === 0) return line;
      return line.replace(/^(#{1,6})(?=\s)/, (hashes) =>
        '#'.repeat(Math.min(6, Math.max(1, hashes.length + by))),
      );
    })
    .join('\n');
}

// One entry per pull request merged into the release branch that a release commit belongs to,
// in merge order, for those whose description has release notes written by someone who can
// write to the repository.
export async function collectPullRequestNotes({ commits, branch, owner, repo, request, logger }) {
  const pulls = new Map();
  for (const commit of commits) {
    const associated = await request(`/repos/${owner}/${repo}/commits/${commit.hash}/pulls`);
    for (const pull of associated) {
      if (pull.merged_at && pull.base?.ref === branch) pulls.set(pull.number, pull);
    }
  }

  const sections = [];
  const merged = [...pulls.values()].sort((a, b) => a.merged_at.localeCompare(b.merged_at));
  for (const pull of merged) {
    const notes = extractReleaseNotes(pull.body);
    if (!notes) continue;
    const login = pull.user?.login;
    const { permission } = login
      ? await request(`/repos/${owner}/${repo}/collaborators/${encodeURIComponent(login)}/permission`)
      : {};
    if (!WRITE_PERMISSIONS.has(permission)) {
      logger.warn(`Leaving out the release notes of #${pull.number}: its author cannot write to ${owner}/${repo}.`);
      continue;
    }
    sections.push({ number: pull.number, title: pull.title, url: pull.html_url, notes });
  }
  return sections;
}

// The commit notes' version heading, then each pull request's section, then the commit list under
// "All changes". The commit notes unchanged when no pull request has any.
export function composeNotes(commitNotes, sections) {
  if (sections.length === 0) return commitNotes;
  const [heading, ...rest] = commitNotes.split('\n');
  const highlights = sections.map(
    (s) => `## ${headingFor(s.title)} ([#${s.number}](${s.url}))\n\n${s.notes}`,
  );
  return [heading, ...highlights, `## All changes\n\n${rest.join('\n').trim()}`].join('\n\n') + '\n';
}

// A pull request title without its conventional-commit type, starting with a capital.
function headingFor(title) {
  const subject = title.replace(/^\w+(\([^)]*\))?!?:\s*/, '');
  return subject.charAt(0).toUpperCase() + subject.slice(1);
}

function repositoryOf(context) {
  const fromEnv = context.env.GITHUB_REPOSITORY;
  const slug = fromEnv ?? /github\.com[/:]([^/]+\/[^/]+?)(\.git)?$/.exec(context.options.repositoryUrl)?.[1];
  if (!slug) throw new Error(`Cannot tell the GitHub repository from ${context.options.repositoryUrl}.`);
  const [owner, repo] = slug.split('/');
  return { owner, repo };
}

function gitHubRequest(env) {
  const token = env.GITHUB_TOKEN ?? env.GH_TOKEN;
  if (!token) throw new Error('Reading pull request release notes needs GITHUB_TOKEN.');
  const api = env.GITHUB_API_URL ?? 'https://api.github.com';
  return async (path) => {
    const response = await fetch(`${api}${path}`, {
      headers: {
        accept: 'application/vnd.github+json',
        authorization: `Bearer ${token}`,
        'x-github-api-version': '2022-11-28',
      },
    });
    if (!response.ok) throw new Error(`GitHub answered ${response.status} to GET ${path}.`);
    return response.json();
  };
}
