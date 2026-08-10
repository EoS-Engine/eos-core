using EOS.Contracts;

namespace EOS.Learning.Tests;

public class IntegrityHashCalculatorTests
{
    [Fact]
    public void Compute_IsDeterministic_ForTheSameInputs()
    {
        var recordId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        var first = IntegrityHashCalculator.Compute(
            recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", ["adr-1"], occurredAt);
        var second = IntegrityHashCalculator.Compute(
            recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", ["adr-1"], occurredAt);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_IsSensitiveToEveryField()
    {
        var recordId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        var baseline = IntegrityHashCalculator.Compute(
            recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", ["adr-1"], occurredAt);

        Assert.NotEqual(baseline, IntegrityHashCalculator.Compute(
            Guid.NewGuid(), PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", ["adr-1"], occurredAt));
        Assert.NotEqual(baseline, IntegrityHashCalculator.Compute(
            recordId, PipelineStage.BestPractice, PipelineStage.BestPractice, "PrincipalEngineer", ["adr-1"], occurredAt));
        Assert.NotEqual(baseline, IntegrityHashCalculator.Compute(
            recordId, PipelineStage.Pattern, PipelineStage.Principle, "PrincipalEngineer", ["adr-1"], occurredAt));
        Assert.NotEqual(baseline, IntegrityHashCalculator.Compute(
            recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "SomeoneElse", ["adr-1"], occurredAt));
        Assert.NotEqual(baseline, IntegrityHashCalculator.Compute(
            recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", ["adr-2"], occurredAt));
        Assert.NotEqual(baseline, IntegrityHashCalculator.Compute(
            recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", ["adr-1"], occurredAt.AddSeconds(1)));
    }

    [Fact]
    public void Compute_CanonicalizesEvidenceRefsAsAStableJsonArray()
    {
        var recordId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;

        var first = IntegrityHashCalculator.Compute(
            recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", ["adr-1", "adr-2"], occurredAt);
        var second = IntegrityHashCalculator.Compute(
            recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", ["adr-1", "adr-2"], occurredAt);
        var reordered = IntegrityHashCalculator.Compute(
            recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", ["adr-2", "adr-1"], occurredAt);

        Assert.Equal(first, second);
        Assert.NotEqual(first, reordered); // order is preserved as given, not normalized away
    }

    [Fact]
    public void Compute_CanonicalizesOccurredAt_RegardlessOfOriginalOffset()
    {
        var recordId = Guid.NewGuid();
        var utcInstant = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var equivalentWithOffset = utcInstant.ToOffset(TimeSpan.FromHours(5));

        var fromUtc = IntegrityHashCalculator.Compute(
            recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", [], utcInstant);
        var fromOffset = IntegrityHashCalculator.Compute(
            recordId, PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", [], equivalentWithOffset);

        // Same instant in time, expressed with a different offset, must hash identically —
        // "normalize to UTC" (WP-027 Decision 3).
        Assert.Equal(fromUtc, fromOffset);
    }

    [Fact]
    public void Compute_TransitionRecordOverload_MatchesTheFieldOverload()
    {
        var transition = new TransitionRecord(
            TransitionId: Guid.NewGuid(),
            RecordId: Guid.NewGuid(),
            FromStage: PipelineStage.Principle,
            ToStage: PipelineStage.GoldenPath,
            TriggeredBy: "StageEngine",
            EvidenceRefs: ["template-ref"],
            IntegrityHash: "placeholder",
            OccurredAt: DateTimeOffset.UtcNow);

        var expected = IntegrityHashCalculator.Compute(
            transition.RecordId, transition.FromStage, transition.ToStage, transition.TriggeredBy,
            transition.EvidenceRefs, transition.OccurredAt);

        Assert.Equal(expected, IntegrityHashCalculator.Compute(transition));
    }

    [Fact]
    public void Compute_ReturnsALowercaseHexSha256String()
    {
        var hash = IntegrityHashCalculator.Compute(
            Guid.NewGuid(), PipelineStage.Pattern, PipelineStage.BestPractice, "PrincipalEngineer", [], DateTimeOffset.UtcNow);

        Assert.Equal(64, hash.Length); // SHA-256 = 32 bytes = 64 hex characters
        Assert.Equal(hash, hash.ToLowerInvariant());
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }
}
