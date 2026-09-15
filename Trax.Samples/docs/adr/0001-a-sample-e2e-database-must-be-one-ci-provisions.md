---
authors: [Theauxm]
areas: [testing, ci]
status: accepted
---

# A sample's E2E database must be one CI actually provisions

A sample E2E factory that boots against PostgreSQL must name a port and a database CI
actually provisions: a service container on a fixed host port, plus an explicit
create-databases list. Not every sample uses Postgres. ChatService runs on SQLite, the
GraphQLClient billing and inventory servers run on the in-memory provider, and
PersistedOperations reads its connection from configuration.

## Status

**Accepted.**

## Why this is written down

Because getting it wrong does not fail. It reports success.

A factory pointing at a database CI does not provision either fails to connect, which is
loud and fine, or it skips, which is not. The Bookworm suite once hid a zero-coverage gap
behind a local-only port 5433: the tests were green, the build was green, and nothing ran.
A suite that silently tests nothing is worse than no suite, because it is counted.

## Considered options

**Let each sample provision its own database in a fixture.** More self-contained, and it
moves the failure from "cannot connect" to "created a database nobody expected", which is
just as silent. It also multiplies the setup cost across a dozen samples.

**Point everything at one database.** Simplest, and rejected because the samples are
deliberately different topologies (`LocalWorkers`, `DistributedWorkers`, `EphemeralWorkers`)
and sharing one schema between them couples samples that are meant to demonstrate
independence.

## Consequences

**The CI workflow is the source of truth, and the guard reads it.** It parses the host ports
mapped onto the service container and the databases created in the setup loop, then checks
every factory against that. Changing the workflow and forgetting a sample fails, and so does
the reverse.

**A new sample needs a line in the workflow before its suite means anything.** That is the
intended friction: the alternative is a suite that looks like coverage and is not.

## Exemplars

- `E2EDatabaseProvisioningTests` parses `.github/workflows/pull_request.yml` for the
  provisioned ports and databases and pins the Postgres factories' default connection strings
  to them, so the two cannot drift apart unnoticed.

Not covered:

- The guard checks the connection string a factory *defaults* to. A suite that overrides it
  at run time, or reads it from configuration, is outside what the regex sees.
- It finds a factory by a `/Factories/` path segment or a `*Factory.cs` file name. A
  connection string that lives in neither, in a fixture or a test host, is skipped entirely.
  All five today are in `Factories/` folders, so widening the match to the file name changed
  nothing about what is covered.
- The floor assertion is a fixed number, not a count of the factories that exist. It is set to
  the five connection strings present today, so deleting a suite fails it until someone lowers
  the number deliberately, and adding one does not raise it.
- Nothing checks `docker-compose.yml`, which provisions a superset of them for local runs,
  the workspace's other suites included. It
  is kept in step with the workflow by hand, and had already drifted: `bookworm_e2e_tests` was
  missing from its list while CI created it, so the Bookworm suite took the skip path
  everywhere except CI, which is the exact failure this ADR exists to prevent.

## Changelog

- **2026-09-11**: Corrected the non-Postgres samples. The billing and inventory servers are
  GraphQLClient's, not Bookworm's, and they run on the in-memory provider rather than on no
  database; Bookworm is the Postgres sample this ADR is about.
- **2026-09-11**: Widened the factory scan to match a `*Factory.cs` file name as well as a
  `Factories/` folder, raised the floor to the five connection strings that exist, and recorded
  that `docker-compose.yml` is unguarded and had already drifted.
- **2026-09-11**: Recorded.
