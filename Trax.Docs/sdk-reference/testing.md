---
title: Testing
description: Index of the test-support packages Trax ships for your own test projects, with the types and members of each architecture-guard package.
type: landing
area: testing
parent: SDK Reference
has_children: true
nav_order: 14
---

# Testing

The test-support packages Trax ships for consumers' own test projects. The architecture-guard fixtures they contain are described as a set in [Architecture Guards](/docs/reference/architecture-guards); these pages list each package's types and members.

```csharp
[TestFixture]
public sealed class HygieneGuards : HygieneGuardFixture { }
```

| Page | Package | Description |
|------|---------|-------------|
| [Trax.Core.Testing](/docs/sdk-reference/testing/core-testing) | Trax.Core.Testing | Guard infrastructure (`ArchitectureGuardOptions`, `GuardResult`, `RepoRoot`, `SourceFiles`, `SourceText`), the hygiene and repo-convention guards and fixtures, and the vocabulary guard |
