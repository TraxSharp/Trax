---
layout: default
title: Semantic Release
parent: Reference
nav_order: 5
---

# Semantic Release

Trax.Core uses [semantic-release](https://github.com/semantic-release/semantic-release) to automatically version releases. When you push to `main`, it analyzes commits, determines the next version, updates `Directory.Build.props` and `CHANGELOG.md`, and publishes to NuGet.

## Commit Messages

Semantic-release reads your commit messages using the [Conventional Commits](https://www.conventionalcommits.org/) format:

```
type(scope): description
```

For example:

```
feat(scheduler): add support for cron expressions
fix(effect): handle null in effect metadata
docs: update usage guide
```

The `type` tells semantic-release whether you've added a feature, fixed a bug, or just updated docs. Here's what triggers a release:

- `feat:` New feature, bumps minor version (5.1.0 → 5.2.0)
- `fix:` Bug fix, bumps patch version (5.1.0 → 5.1.1)
- `perf:` Performance improvement, bumps patch version
- `refactor:` Code refactoring, bumps patch version
- `docs:`, `test:`, `chore:`, `ci:` No release triggered

For breaking changes, add `BREAKING CHANGE:` anywhere in the commit body:

```
feat(effect): redesign metadata API

The EffectMetadata structure has changed. Use the new 
EffectMetadataV2 constructor instead.

BREAKING CHANGE: EffectMetadata is no longer compatible with 1.x
```

This triggers a major version bump (1.1.0 → 2.0.0).

## How It Works

When you push a commit to `main`, GitHub Actions runs the release train:

1. **Analyze commits** since the last tag (e.g., `v5.1.0`)
2. **Determine the next version** based on commit types
3. **Write version to `.release-version`** file (used by CI to pass to `dotnet pack -p:Version=X.Y.Z`)
4. **Update `CHANGELOG.md`** with release notes from commits
5. **Create a GitHub release** with changelog and git tag
6. **Build and publish** NuGet packages (CI reads `.release-version` and passes it to `dotnet pack`)

> **Note:** `Directory.Build.props` is **not** updated by semantic-release. It stays permanently at `1.99.99` for local development. The actual package version comes from the git tag and `.release-version` file, passed to `dotnet pack -p:Version=X.Y.Z` by CI.

All of this happens in the `.github/trains/nuget_release.yml` train. The configuration lives in `.releaserc.json`.

## Examples

A feature and a bug fix in one push:

```
feat(mediator): cache train discovery results
fix(effect): prevent memory leak in effect tracking
```

Result: minor version bump (5.1.0 → 5.2.0).

Documentation only:

```
docs: rewrite the scheduler guide
```

Result: no release (version stays 5.1.0).

Breaking change:

```
feat(scheduler): rewrite job execution engine

BREAKING CHANGE: JobConfiguration is no longer public
```

Result: major version bump (5.1.0 → 6.0.0).

## What Gets Updated

After a release, you'll see:

- **GitHub release page** with changelog (https://github.com/Theauxm/Trax.Core/releases)
- **CHANGELOG.md** updated with release notes
- **NuGet packages** available on https://www.nuget.org/packages?q=Trax.Core
- **Git tag** (e.g., `v5.2.0`) marking the release commit

## Troubleshooting

No release after push: Check that commits follow `type(scope): description` format and are pushed to `main`.

NuGet publish failed: Verify `NUGET_API_KEY` is configured in GitHub repository settings. Check train logs for details.
