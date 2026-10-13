---
authors: [Theauxm]
repos: [samples]
areas: [testing, packaging, ci]
status: accepted
---

# Browser tests use Playwright for .NET, in test projects only

The proof that a machine is served as a typed API ([0048](./0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md))
is a person's browser driving it: the request a page makes, the push it renders, the request it does not make while it
waits. No existing test sees that, because the samples' E2E suites speak HTTP to the host and the client's own tests
run against a fake server. Trax adds [Playwright for .NET](https://github.com/microsoft/playwright-dotnet)
(`Microsoft.Playwright`) as a test dependency, for browser tests in `Trax.Samples/tests/` projects, starting with
`Trax.Samples.Recovery.Browser`.

## Status

**Accepted.**

## The rules

- **Test projects only.** A project that references `Microsoft.Playwright` sets `<IsTestProject>true</IsTestProject>`.
  Nothing packed carries it, so no consumer depends on it.
- **One version, the one the screenshot script uses.** `Trax.Samples/scripts/screenshots` already drives the samples
  with Playwright for Node at 1.63.0. The NuGet package is pinned in the root `Directory.Packages.props` at the same
  version, so both download the same browser build and one upgrade moves both.
- **CI installs chromium only**, after the build, with the script the package ships (`playwright.ps1 install
  --with-deps chromium`). The browser is downloaded from Playwright's own CDN at the revision the pinned package
  names; it is not a NuGet package and is not in a lockfile, so the package version is the pin.
- **The database is one CI provisions** (samples/0001), named in the folder's `databases` list.
- **No sleeps** ([0006](./0006-tests-synchronise-on-a-signal.md)): a browser test waits on a locator, a request or a
  console event, never on a delay, and owns its timeouts ([0014](./0014-a-test-owns-every-timeout-it-waits-behind.md)).

## Considered options

**bUnit or jsdom only.** They render components without a browser and without the network, so they cannot show that
a push arrived with no polling request, or that an ad-hoc document is refused on the wire. They stay the unit tests.

**Playwright for Node, beside the screenshot script.** One tool, already present. Rejected: the tests would run
outside `dotnet test`, so they could not reuse the samples' host fixtures (`RecoveryHostProcess`, `TestPostgres`,
`[CheckTraxInvariants]`) or report in the same run, and CI would need a second test runner per folder.

**Selenium.** Needs a separately versioned driver per browser and has no network interception built in.

## Why a licence that is not MIT is fine here

Playwright is Apache-2.0, like CsCheck ([0044](./0044-property-tests-use-cscheck-in-test-projects-only.md)) and
AwesomeAssertions. Its obligations attach to redistribution, and a test project is never packed or published, so
nothing Trax ships carries it. The reasoning holds only while the boundary holds, which is why the boundary is the
guarded part.

## Exemplars

**Enforced elsewhere:** in `Trax.Samples.Tests.Meta`, `NoTestFrameworkInSrcTests.OnlyTestProjects_ReferencePlaywright`
fails on a `Microsoft.Playwright` reference from any project that is not a test project, and
`PlaywrightVersionTests.The_NuGet_and_npm_Playwright_versions_match` fails when the root pin and the screenshot
script's `package.json` disagree.

Not covered: nothing checks that a browser test waits on a signal rather than a delay; review does, as for every other
test.

## Changelog

- **2026-10-09**: Recorded.
