global using AwesomeAssertions;
global using NUnit.Framework;

// One host process and one browser serve every test, and a test reads the database the host writes.
[assembly: NonParallelizable]
