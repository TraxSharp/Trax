# Security Policy

## Reporting a vulnerability

Report security issues privately through GitHub's [private vulnerability reporting](https://github.com/TraxSharp/Trax.Mediator/security/advisories/new), not a public issue. We aim to acknowledge within 3 business days.

Where possible, include the affected package and version, a description, reproduction steps or a proof of concept, and the impact.

## Supported versions

Fixes ship against the latest version published to NuGet. Older major versions are not backported.

## Supply-chain posture

Trax builds and publishes through a defense-in-depth pipeline: SHA-pinned actions, isolated release credentials, OIDC trusted publishing with SLSA provenance, and locked + audited dependencies (a known-vulnerability advisory fails the build). See the [Supply Chain Security](https://traxsharp.net/docs/supply-chain-security) guide.
