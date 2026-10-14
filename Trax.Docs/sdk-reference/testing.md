---
title: Testing
description: "Index of the test-support packages Trax ships for your own test projects: the architecture guards, database invariants and machine conformance checks."
type: landing
area: testing
parent: SDK Reference
has_children: true
nav_order: 14
---

# Testing

The test-support packages Trax ships for a consumer's own test projects. The architecture-guard packages share `ArchitectureGuardOptions` and `GuardResult` from Trax.Core.Testing; [Architecture Guards](/docs/reference/architecture-guards) describes them as a set.

| Package | Description |
|---------|-------------|
| [Trax.Core.Testing](/docs/sdk-reference/testing/core-testing) | Guard infrastructure (`ArchitectureGuardOptions`, `GuardResult`, `RepoRoot`, `SourceFiles`, `SourceText`), the hygiene, repo-convention and vocabulary guards, and their fixtures |
| [Trax.Effect.Data.Testing](/docs/sdk-reference/testing/effect-data-testing) | Data-layer guards and fixture, the owner-scope census, and `TraxInvariants` |
| [Trax.Mediator.Testing](/docs/sdk-reference/testing/mediator-testing) | The guard that every train has its own train interface |
| [Trax.Api.GraphQL.Testing](/docs/sdk-reference/testing/graphql-testing) | Cross-schema GraphQL guards and fixture |
| [Trax.Effect.StateMachine.Testing](/docs/sdk-reference/testing/effect-statemachine-testing) | `MachineConformance` and `DifferentialCorpus` |
| [Trax.Api.Auth.Jwt.Testing](/docs/sdk-reference/api-auth/test-jwks-server) | Signed test tokens for JWT-authenticated hosts |
