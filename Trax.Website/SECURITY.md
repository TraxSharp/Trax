# Security Policy

## Reporting a vulnerability

Report security issues privately through GitHub's [private vulnerability reporting](https://github.com/TraxSharp/Trax/security/advisories/new), not a public issue. We aim to acknowledge within 3 business days.

Where possible, include the affected page or URL, a description, reproduction steps or a proof of concept, and the impact.

## Supported versions

Only the site as deployed at [traxsharp.net](https://traxsharp.net), built from `main`, is supported.

## Supply-chain posture

The site builds through SHA-pinned actions, installs from the committed lockfile with install scripts disabled in CI, and fails a build whose runtime dependencies carry a high-severity advisory. The docs pages are rendered from [Trax.Docs](https://github.com/TraxSharp/Trax/tree/main/Trax.Docs) `main`. See the [Supply Chain Security](https://traxsharp.net/docs/supply-chain-security) guide.
