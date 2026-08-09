using Xunit;

// Mirrors EOS.Infrastructure.Tests' identical precedent: Scheduler/ExecutionCoordinator tests
// query DispatchedTask state globally across the whole real SQL Server table (there is only one
// Priority Queue/Concurrency ceiling, matching real Scheduler semantics) — running test classes
// in parallel would let them observe each other's in-flight rows.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
