global using AwesomeAssertions;
global using NUnit.Framework;

// One scheduler host serves the whole suite, and several tests steer the same manifests.
[assembly: NonParallelizable]
