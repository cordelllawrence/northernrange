using Xunit;

// Several tests mutate process-global state (environment variables, the current
// directory, temp config files). Run tests serially to keep them deterministic.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
