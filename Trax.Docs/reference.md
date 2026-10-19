---
title: Reference
description: "Index of the reference pages outside the SDK reference: packages, glossary, errors, scheduler trains, effects, tooling and schema migrations."
type: landing
area: general
section: Reference
nav_order: 1
---

# Reference

Pages to look things up in. Per-type and per-method pages are in the
[SDK Reference](/docs/sdk-reference); the pages here cover what spans several types.

## General

- [Packages](/docs/reference/packages): every Trax package, what it is for and what it needs beside it.
- [Glossary](/docs/reference/glossary): the names Trax uses, with the namespace each lives in.
- [Registration Order](/docs/reference/registration-order): which registration calls must come first, and the error each wrong order raises.
- [Troubleshooting](/docs/cross-cutting/troubleshooting): the error messages Trax raises, verbatim, with causes and fixes.
- [Benchmarks](/docs/reference/benchmarks): train overhead against plain code, measured.
- [Acknowledgments](/docs/reference/acknowledgments): the third-party packages Trax depends on.

## Trains

- [Trains & Junctions](/docs/core/trains-and-junctions): writing trains and junctions, and the startup chain check.
- [Building Chains](/docs/core/building-chains): `Chain`, `Resolve`, `ShortCircuit`, `Extract` and `AddServices` in `Junctions()`.
- [Decisions](/docs/core/decisions): tracks, deciders and how decisions are verified at startup.
- [Cancellation Tokens](/docs/cross-cutting/cancellation-tokens): how a token flows through a run.
- [Route and Junction Registration](/docs/sdk-reference/trains-and-junctions/route-registration): the `AddTraxRoute` and `AddTraxJunction` methods.

## Effects

- [Metadata](/docs/effect/metadata): the record every run produces.
- [Host Tracking](/docs/effect/host-tracking): which host a run ran on.
- [JSON Effect](/docs/effect/effect-providers/json-effect): logging tracked model changes in development.
- [Parameter Effect](/docs/effect/effect-providers/parameter-effect): storing inputs and outputs on Metadata.
- [Junction Logger](/docs/effect/effect-providers/junction-logger): per-junction metadata.
- [Junction Progress](/docs/effect/effect-providers/junction-progress): the running junction and cancellation between junctions.
- [Junction Events](/docs/effect/junction-events): each step of a run, published live and stored.
- [Decision Recording and Models](/docs/effect/decisions): how decisions are recorded and replayed.

## Data

- [Data Persistence](/docs/effect/effect-providers/data-persistence): the Postgres, SQLite and InMemory data providers.
- [Schema migrations](/docs/reference/schema-migrations): every schema migration that changes something visible, Postgres and SQLite paired.

## Mediator

- [Train Discovery](/docs/mediator/train-discovery): how trains are found and routed by input type.

## Scheduling

- [Scheduling Options](/docs/scheduler/scheduling-options): manifests, groups and schedule types.
- [Exclusion Windows](/docs/scheduler/exclusions): days, dates and time windows a manifest skips.
- [ManifestManager](/docs/scheduler/admin-trains/manifest-manager), [JobDispatcher](/docs/scheduler/admin-trains/job-dispatcher), [JobRunner](/docs/scheduler/admin-trains/job-runner) and [MetadataCleanup](/docs/scheduler/admin-trains/metadata-cleanup): the scheduler's own trains.

## GraphQL API and dashboard

- [GraphQL Client](/docs/api-graphql-client): the outbound GraphQL client.
- [Operations console](/docs/dashboard): the operations console and its pages.

## Security

- [Authorization](/docs/authorization): endpoint policies, `TraxAuthorize`, `TraxAllowAnonymous` and startup validation.
- [Security policy](/docs/SECURITY): reporting a vulnerability and supported versions.

## Tooling

- [CLI](/docs/reference/cli): the `trax` command, `generate` and `machine`.
- [Project Templates](/docs/reference/templates): the `dotnet new` templates.
- [Architecture Guards](/docs/reference/architecture-guards): the guard packages, their fixtures and what they check.
- [Analyzer](/docs/core/analyzer): the deprecated chain analyzer.
- [IDE Extensions](/docs/core/ide-extensions): inlay hints for VS Code and Rider.
