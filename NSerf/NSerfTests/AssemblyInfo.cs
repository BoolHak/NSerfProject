using Xunit;

// Disable parallel test execution to prevent socket binding conflicts in integration tests
[assembly: CollectionBehavior(DisableTestParallelization = true)]
