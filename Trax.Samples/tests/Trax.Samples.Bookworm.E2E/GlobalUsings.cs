global using AwesomeAssertions;
global using NUnit.Framework;

// E2E suites share one WebApplicationFactory and a single PostgreSQL database; running them in
// parallel would cross-contaminate state.
[assembly: NonParallelizable]
