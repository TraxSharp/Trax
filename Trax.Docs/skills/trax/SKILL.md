---
name: trax
description: Use before writing or changing code in a .NET app that references Trax packages (Trax.Core, Trax.Effect, Trax.Mediator, Trax.Scheduler, Trax.Api, Trax.StateMachine) or the @traxsharp/state-machine npm package. Says what Trax is, which packages and template to use, the rules that hold everywhere, how to check these skills match the installed release, and which Trax skill to load for the task.
metadata:
  internal: true
---

# Trax

Trax .NET is a headless web framework. Business work is a **train**: a class that runs a chain of small typed steps
called **junctions**, where a failing junction stops the rest. Everything else attaches to that one class. The
database records every run, the mediator and scheduler decide when and where it runs, the GraphQL API exposes it, and
a state machine walks a user through a longer process that runs trains along the way.
[What Trax does](https://traxsharp.net/docs/what-trax-does) maps each capability to its page, and the
[Glossary](https://traxsharp.net/docs/reference/glossary) gives each Trax name its plain meaning.

Every docs page is also served as raw markdown: append `.md` to its URL.
[llms.txt](https://traxsharp.net/llms.txt) lists every page, with full-text bundles per section.

## First: check these skills match the app

This file's `metadata.trax-version` is the Trax release these skills were written for. Find the app's Trax version
in `Directory.Packages.props` (or the `.csproj` if it does not use central package management): every `Trax.*`
package is at one version.

- **Same version:** go on.
- **The app is older:** the skills may describe APIs it does not have. Tell the user, and suggest installing the
  skills from the app's release:
  `npx skills add https://github.com/TraxSharp/Trax/tree/skills-v<version>` (add `-a claude-code` for Claude Code,
  whose plugin always follows the latest release). A `skills-v<version>` tag exists only for a release that
  published skills, from the first one that did; an app on an earlier release has none. For such an app, keep
  the skills from the `skills` branch and check each API they describe against the app's version: the
  [migration guides](https://traxsharp.net/docs/migration-guides) say what changed in each release, and
  `trax-upgrade` moves the app forward if the user wants that.
- **The app is newer:** update the skills (`npx skills update`, or `/plugin update trax@traxsharp` in Claude Code).
- **The user wants to move the app to a newer Trax:** load `trax-upgrade`.

## Rules that hold everywhere

- **Every Trax package is at one version.** A new `Trax.*` package goes in at the version the others already have.
  The packages target `net10.0` only.
- **Use Trax's own types where Trax has the concept.** `Either<L, R>` and `Unit` come from `Trax.Core.Functional`;
  do not add a functional library for them. A train or query model is gated with `[TraxAuthorize]` or opened with
  `[TraxAllowAnonymous]`; HotChocolate's `[Authorize]` is refused in their place. When you need a type for something
  Trax does, look for Trax's first.
- **The host checks itself at startup.** A chain that can never run, a registration in the wrong order or a missing
  provider makes the host refuse to start, and the message names the cause and usually the page that fixes it.
  Read it before changing anything. [Registration order](https://traxsharp.net/docs/reference/registration-order)
  lists what must come before what.
- **Security defaults are closed on purpose.** Never turn off authentication, authorization, error masking,
  persisted-operation requirements or an Origin check to make something work. If a request is refused, find out
  why; if the fix really is to open something, ask the user first.
- **Follow the docs over the code you find.** An app can drift from the documented layout. When existing code
  disagrees with a Trax docs page, the page is the pattern; say so rather than copy the drift.

## Which packages

Pick the highest layer you need, then one data provider. Each package brings the layers below it.
[Packages](https://traxsharp.net/docs/reference/packages) lists every one with the namespace to import.

| You want | Install |
|---|---|
| Typed pipelines only, no DI, no database | `Trax.Core` |
| Trains recorded in a database | `Trax.Effect` and one of `Trax.Effect.Data.Postgres`, `.Sqlite`, `.InMemory` |
| Dispatch by input type, the startup chain check | add `Trax.Mediator` |
| Schedules, a work queue, retries, dead letters | add `Trax.Scheduler` |
| A GraphQL API over the trains | add `Trax.Api.GraphQL` and an auth package (`Trax.Api.Auth.ApiKey`, `.Jwt`, `.Oidc`) |
| The operations console | add `Trax.Api.Dashboard` |
| State machines | `Trax.StateMachine.Persistence` (server), `@traxsharp/state-machine` (TypeScript client) |

## Before writing code

Read these, in order, the first time you work in a Trax app. They are short, and each one decides something the
code depends on.

1. [Getting started](https://traxsharp.net/docs/getting-started): one train from nothing to GraphQL.
2. [Project templates](https://traxsharp.net/docs/reference/templates): which template, and what it generates.
3. [Organize trains in a project](https://traxsharp.net/docs/core/organize-trains): where every file goes.
4. [Train or machine](https://traxsharp.net/docs/statemachine/train-or-machine), if the work spans more than one
   request.

## Which skill next

| Task | Skill |
|---|---|
| Start a new app, or add Trax to one | `trax-start-a-project` |
| Write or change a train or junction | `trax-write-a-train` |
| A train has to choose between paths, or a rule changes often | `trax-write-a-decider` |
| A process spans several requests or users, or waits on people | `trax-design-a-machine` |
| Expose trains or machines over GraphQL, set up auth | `trax-expose-over-api` |
| Call the API from a frontend, generate a client | `trax-generate-a-client` |
| Run trains on a schedule, in a queue, or on workers | `trax-schedule-work` |
| Test any of the above | `trax-test-your-app` |
| Move the app to a newer Trax | `trax-upgrade` |
| The host will not start, or a run fails and the cause is unclear | `trax-troubleshoot` |
