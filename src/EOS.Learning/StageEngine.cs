using EOS.Contracts;

namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §16's remaining stage-promotion edges (Lesson→Pattern is
/// WP-026's <c>ClusterTrigger</c>): <c>Pattern→BestPractice→Principle→GoldenPath→Automation→
/// ReusableComponent→PlatformCapability</c>. Every method is idempotent (already-at-or-beyond
/// target stage is a no-op, matching <c>ClusterTrigger</c>'s own established precedent) and
/// persist-then-publish (event fires only after both the <see cref="PipelineRecord"/> stage
/// write and the <see cref="TransitionRecord"/> insert succeed).
///
/// Event note (evidence-based, not invented): the spec names exactly five promotion events for
/// seven Lesson→PlatformCapability edges (<c>LessonPromoted</c>, <c>BestPracticeRatified</c>,
/// <c>PrincipleGeneralized</c>, <c>GoldenPathCodified</c>, <c>PlatformCapabilityPipelineAdvanced</c>)
/// — §11.5's own pseudocode ("<c>on PlatformCapabilityPipelineAdvanced(record)</c>") confirms
/// that event fires specifically on reaching <c>PlatformCapability</c>. <c>GoldenPath→Automation</c>
/// and <c>Automation→ReusableComponent</c> therefore have no dedicated event of their own — only
/// a <see cref="TransitionRecord"/>, no invented event name.
/// </summary>
public sealed class StageEngine(
    IPipelineRecordStore pipelineRecordStore,
    ITransitionRecordStore transitionRecordStore,
    RoiGate roiGate,
    IBestPracticeRatifiedEventPublisher bestPracticeRatifiedEventPublisher,
    IPrincipleGeneralizedEventPublisher principleGeneralizedEventPublisher,
    IGoldenPathCodifiedEventPublisher goldenPathCodifiedEventPublisher,
    IPlatformCapabilityPipelineAdvancedEventPublisher platformCapabilityPipelineAdvancedEventPublisher,
    int domainGeneralizationMinimumCount)
{
    /// <summary>Pattern → BestPractice: "Principal Engineer ratification, ADR-linked" (§16) — <paramref name="ratifyingRole"/>/<paramref name="adrReference"/> are trusted caller-supplied metadata, matching WP-027 Decision 5's authority posture.</summary>
    public async Task<StagePromotionResult> PromoteToBestPracticeAsync(
        Guid recordId, string adrReference, string ratifyingRole, CancellationToken cancellationToken = default)
    {
        var record = await RequireStageAsync(recordId, PipelineStage.Pattern, PipelineStage.BestPractice, cancellationToken);
        if (record is null)
        {
            return NoOp(PipelineStage.BestPractice);
        }

        if (string.IsNullOrWhiteSpace(adrReference))
        {
            throw new ArgumentException("adrReference must not be empty.", nameof(adrReference));
        }

        if (string.IsNullOrWhiteSpace(ratifyingRole))
        {
            throw new ArgumentException("ratifyingRole must not be empty.", nameof(ratifyingRole));
        }

        await pipelineRecordStore.UpdateApprovalRefsAsync(recordId, [.. record.ApprovalRefs, adrReference], cancellationToken);
        await PersistTransitionAsync(record, PipelineStage.BestPractice, ratifyingRole, [adrReference], cancellationToken);
        bestPracticeRatifiedEventPublisher.PublishBestPracticeRatified(recordId);

        return new StagePromotionResult(true, PipelineStage.BestPractice, "Ratified.");
    }

    /// <summary>BestPractice → Principle: "generalizes across ≥2 domains" (§16) — WP-027 Decision 7 (locked): domain count threshold sourced from <c>Thresholds.json</c> (<c>domainGeneralizationMinimumCount</c>, default 2).</summary>
    public async Task<StagePromotionResult> PromoteToPrincipleAsync(
        Guid recordId, string[] distinctDomains, CancellationToken cancellationToken = default)
    {
        var record = await RequireStageAsync(recordId, PipelineStage.BestPractice, PipelineStage.Principle, cancellationToken);
        if (record is null)
        {
            return NoOp(PipelineStage.Principle);
        }

        // CodeRabbit R1 finding #7: trim and drop blanks before counting, so surrounding
        // whitespace (" backend " vs "backend") or empty entries can't inflate the domain count.
        var domainCount = distinctDomains
            .Select(domain => domain.Trim())
            .Where(domain => domain.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (domainCount < domainGeneralizationMinimumCount)
        {
            return new StagePromotionResult(
                false, null,
                $"Generalizes across {domainCount} domain(s); requires at least {domainGeneralizationMinimumCount}.");
        }

        await PersistTransitionAsync(record, PipelineStage.Principle, "StageEngine", distinctDomains, cancellationToken);
        principleGeneralizedEventPublisher.PublishPrincipleGeneralized(recordId);

        return new StagePromotionResult(true, PipelineStage.Principle, "Generalized across sufficient domains.");
    }

    /// <summary>Principle → GoldenPath: "codified as a reusable, gated implementation template in EOS.Tools" (§16) — <paramref name="templateReference"/> is caller-supplied evidence; no automated codification mechanism exists anywhere in this repository (EOS.Tools is an empty scaffold).</summary>
    public async Task<StagePromotionResult> PromoteToGoldenPathAsync(
        Guid recordId, string templateReference, CancellationToken cancellationToken = default)
    {
        var record = await RequireStageAsync(recordId, PipelineStage.Principle, PipelineStage.GoldenPath, cancellationToken);
        if (record is null)
        {
            return NoOp(PipelineStage.GoldenPath);
        }

        if (string.IsNullOrWhiteSpace(templateReference))
        {
            throw new ArgumentException("templateReference must not be empty.", nameof(templateReference));
        }

        await PersistTransitionAsync(record, PipelineStage.GoldenPath, "StageEngine", [templateReference], cancellationToken);
        goldenPathCodifiedEventPublisher.PublishGoldenPathCodified(recordId);

        return new StagePromotionResult(true, PipelineStage.GoldenPath, "Codified.");
    }

    /// <summary>
    /// GoldenPath → Automation: gated by the ROI Gate (§11.3, INV-3). WP-027 Decision 1 (locked):
    /// <see cref="RoiGate"/> never returns a promotable decision (no <c>roi_minimum</c> exists
    /// anywhere), so this method never actually promotes — it always returns a non-promoted
    /// result while still honestly recording the evaluation attempt via
    /// <see cref="IPipelineRecordStore.UpdateRoiEvaluationRefAsync"/>. <see cref="AutomationVisibilityPublisher"/>
    /// (WP-027 Decision 2's KnowledgeNode re-typing) is real, independently tested infrastructure
    /// with no production caller yet from this method — matching WP-024/025's own precedent for
    /// components with no reachable call site until a currently-missing piece (here, the ROI
    /// threshold) is supplied — since there is no "promotion succeeded" branch to call it from
    /// until then.
    /// </summary>
    public async Task<StagePromotionResult> PromoteToAutomationAsync(
        Guid recordId, RoiEvaluationInput roiInput, CancellationToken cancellationToken = default)
    {
        var record = await RequireStageAsync(recordId, PipelineStage.GoldenPath, PipelineStage.Automation, cancellationToken);
        if (record is null)
        {
            return NoOp(PipelineStage.Automation);
        }

        var roiResult = roiGate.Evaluate(roiInput);
        await pipelineRecordStore.UpdateRoiEvaluationRefAsync(recordId, Guid.NewGuid().ToString(), cancellationToken);

        // roiResult.Decision is always Denied or ThresholdUnavailable (RoiGateDecision has no
        // "Approved" value) — this is the sole, structural reason no promotion ever occurs here.
        return new StagePromotionResult(false, null, roiResult.Reason);
    }

    /// <summary>Automation → ReusableComponent: "packaged into EOS.SDK or a dedicated shared library" (§16) — <paramref name="packageReference"/> is caller-supplied evidence; no automated packaging mechanism exists anywhere in this repository. Unreachable in practice today (see <see cref="PromoteToAutomationAsync"/>), but real and directly testable in isolation.</summary>
    public async Task<StagePromotionResult> PromoteToReusableComponentAsync(
        Guid recordId, string packageReference, CancellationToken cancellationToken = default)
    {
        var record = await RequireStageAsync(recordId, PipelineStage.Automation, PipelineStage.ReusableComponent, cancellationToken);
        if (record is null)
        {
            return NoOp(PipelineStage.ReusableComponent);
        }

        if (string.IsNullOrWhiteSpace(packageReference))
        {
            throw new ArgumentException("packageReference must not be empty.", nameof(packageReference));
        }

        await PersistTransitionAsync(record, PipelineStage.ReusableComponent, "StageEngine", [packageReference], cancellationToken);
        // No dedicated event for this edge — see class doc comment.

        return new StagePromotionResult(true, PipelineStage.ReusableComponent, "Packaged.");
    }

    /// <summary>ReusableComponent → PlatformCapability: "adopted platform-wide, CapabilityUnlocked" (§16) — the pipeline's terminal successful stage; publishes <c>PlatformCapabilityPipelineAdvanced</c> (§11.5's Feedback Loop Guard subscribes to exactly this).</summary>
    public async Task<StagePromotionResult> PromoteToPlatformCapabilityAsync(
        Guid recordId, string capabilityReference, CancellationToken cancellationToken = default)
    {
        var record = await RequireStageAsync(recordId, PipelineStage.ReusableComponent, PipelineStage.PlatformCapability, cancellationToken);
        if (record is null)
        {
            return NoOp(PipelineStage.PlatformCapability);
        }

        if (string.IsNullOrWhiteSpace(capabilityReference))
        {
            throw new ArgumentException("capabilityReference must not be empty.", nameof(capabilityReference));
        }

        await PersistTransitionAsync(record, PipelineStage.PlatformCapability, "StageEngine", [capabilityReference], cancellationToken);
        platformCapabilityPipelineAdvancedEventPublisher.PublishPlatformCapabilityPipelineAdvanced(recordId);

        return new StagePromotionResult(true, PipelineStage.PlatformCapability, "Adopted platform-wide.");
    }

    private async Task<PipelineRecord?> RequireStageAsync(
        Guid recordId, PipelineStage expectedFrom, PipelineStage target, CancellationToken cancellationToken)
    {
        var record = await pipelineRecordStore.GetByIdAsync(recordId, cancellationToken)
            ?? throw new InvalidOperationException($"PipelineRecord '{recordId}' does not exist.");

        // Already at or beyond the target stage — idempotent no-op, matching ClusterTrigger's
        // own already-promoted precedent (and, unlike the prior version of this guard, genuinely
        // "at-or-beyond" as this class's own doc comment always claimed, not merely "==").
        if (IsAtOrBeyond(record.Stage, target))
        {
            return null;
        }

        // Only reachable when record.Stage is genuinely before target — expectedFrom is
        // enforced here, not against an already-at-or-beyond record.
        if (record.Stage != expectedFrom)
        {
            throw new InvalidOperationException(
                $"PipelineRecord '{recordId}' is at {record.Stage}, not {expectedFrom} — cannot promote to {target}.");
        }

        return record;
    }

    private async Task PersistTransitionAsync(
        PipelineRecord record, PipelineStage toStage, string triggeredBy, string[] evidenceRefs, CancellationToken cancellationToken)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var integrityHash = IntegrityHashCalculator.Compute(record.RecordId, record.Stage, toStage, triggeredBy, evidenceRefs, occurredAt);
        var transition = new TransitionRecord(
            TransitionId: Guid.NewGuid(),
            RecordId: record.RecordId,
            FromStage: record.Stage,
            ToStage: toStage,
            TriggeredBy: triggeredBy,
            EvidenceRefs: evidenceRefs,
            IntegrityHash: integrityHash,
            OccurredAt: occurredAt);

        // CodeRabbit R1 finding #8: persist the immutable TransitionRecord evidence before
        // finalizing the stage promotion — a failed insert here must never leave the record
        // promoted with no transition evidence for IntegrityChecker to ever audit.
        await transitionRecordStore.InsertAsync(transition, cancellationToken);
        await pipelineRecordStore.UpdateStageAsync(record.RecordId, toStage, record.Status, record.ConfidenceScore, cancellationToken);
    }

    private static StagePromotionResult NoOp(PipelineStage stage) =>
        new(false, stage, "Already at or beyond the target stage.");

    // Explicit, local to this class only (kept out of the shared PipelineStateMachine to stay
    // within this correction's strict file-scope boundary) — never relies on PipelineStage's own
    // declared enum order being incidentally correct. The same seven forward stages
    // PipelineStateMachine.IsValidEdge already encodes, linearized once here purely for
    // RequireStageAsync's idempotency guard. Quarantine/Demoted are not PipelineStage values at
    // all, so there is no non-linear element to represent.
    private static readonly IReadOnlyDictionary<PipelineStage, int> StageOrder = new Dictionary<PipelineStage, int>
    {
        [PipelineStage.Lesson] = 0,
        [PipelineStage.Pattern] = 1,
        [PipelineStage.BestPractice] = 2,
        [PipelineStage.Principle] = 3,
        [PipelineStage.GoldenPath] = 4,
        [PipelineStage.Automation] = 5,
        [PipelineStage.ReusableComponent] = 6,
        [PipelineStage.PlatformCapability] = 7,
    };

    private static bool IsAtOrBeyond(PipelineStage current, PipelineStage target) => StageOrder[current] >= StageOrder[target];
}
