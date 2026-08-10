using Xunit;

// Mirrors EOS.Orchestrator.Tests'/EOS.Infrastructure.Tests' identical precedent: PipelineRecord/
// IngestionRateGuardState tests query real, shared SQL Server tables with no per-test isolation
// (no delete-between-tests convention exists anywhere in this codebase) — running test classes
// in parallel would let them observe each other's in-flight rows.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
