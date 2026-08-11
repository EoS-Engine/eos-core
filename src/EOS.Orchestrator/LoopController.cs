using EOS.Contracts;

namespace EOS.Orchestrator;

/// <summary>
/// Autonomous-Engineering-Loop-Specification-v1.0 §7.1's 18-step cycle — steps 1-15 and 18 only
/// (16-17 Self-Evaluate/Improve are WP-029). Every step is a citation into an already-existing,
/// already-tested subsystem capability (§7.2: "sixteen are pure citations") verified directly
/// against this repository, never a fabricated one. WP-028 Decision 5a (locked): this is the
/// sole execution mechanism — no <c>BackgroundService</c>/timer/host process exists anywhere in
/// this codebase, and this class introduces none.
///
/// Step 3 (Retrieve Context) note: no new Composition-Root Adapter was needed for this — Reasoning
/// Engine already owns Context Assembly internally via <see cref="ReasoningContextScope"/>
/// (Reasoning-Engine-Specification-v1.0 §12.1/§13.2), triggered automatically whenever
/// <see cref="ReasoningRequest.ContextScope"/> is supplied. An <c>IContextAssemblyClient</c>
/// adapter duplicating that existing mechanism was considered and deliberately not added — it
/// would be exactly the kind of speculative abstraction this WP's authorization forbids.
///
/// Step 12 (Measure Outcomes) note: WP-028 Decision 7 (locked) — the frozen specification cites
/// Reality Validation (Constitution §0.15, "realized inside Protection") and subsystem KPIs
/// (§16, <c>EOS.SDK</c> Telemetry) for this step, and neither is implemented anywhere in this
/// repository (verified by direct search, not assumed). This class does not invent, simulate, or
/// substitute a capability for it — step 12 is recorded as traversed structurally only, and
/// <see cref="LoopIteration.Outcome"/> is derived exclusively from this class's own terminal
/// control-flow result (<c>"Completed"</c>/<c>"Denied"</c>/<c>"Failed"</c>), never from
/// <see cref="GoalProgress"/> or any other subsystem data.
///
/// Steps 13-15 (Learn/Update Memory/Promote Knowledge) note: Learning Engine has no callable
/// client interface anywhere in this repository (confirmed) — it is reached only via events
/// already independently wired (<c>LessonLearned</c> -> <c>Ingestion</c>) or currently unwired
/// (<c>LessonPromoted</c>/<c>BestPracticeRatified</c>). This class records these steps as
/// traversed without a direct synchronous call, matching the specification's own framing that
/// Memory/Learning "decide," the Loop only "observes" (§12).
/// </summary>
public sealed class LoopController(
    IPlanningClient planningClient,
    IReasoningEngineClient reasoningEngineClient,
    IProtectionClient protectionClient,
    IResourceManagementClient resourceManagementClient,
    Scheduler scheduler,
    ExecutionCoordinator executionCoordinator,
    ProgressMonitor progressMonitor,
    ILoopIterationStore loopIterationStore,
    ILoopIterationStartedEventPublisher loopIterationStartedEventPublisher,
    ILoopIterationCompletedEventPublisher loopIterationCompletedEventPublisher) : ILoopControlClient
{
    // WP-028 Decision 6: TriggerContext.TriggerSource is a free-form string; this is the
    // authoritative, closed mapping to §8's entry steps — File/Git Events are permanently
    // excluded (§8.9) and deliberately absent from this table.
    private static readonly IReadOnlyDictionary<string, int> EntryStepByTriggerSource = new Dictionary<string, int>
    {
        ["UserRequest"] = 2,
        ["ManualRequest"] = 2,
        ["ScheduledTask"] = 8,
        ["LearningOpportunity"] = 13,
        ["KnowledgeUpdate"] = 15,
        ["PerformanceDegradation"] = 1,
        ["Failure"] = 11,
    };

    public async Task RunIterationAsync(TriggerContext trigger, CancellationToken cancellationToken = default)
    {
        if (!EntryStepByTriggerSource.TryGetValue(trigger.TriggerSource, out var entryStep))
        {
            throw new ArgumentException(
                $"'{trigger.TriggerSource}' is not one of the 8 in-scope Trigger Sources (§8).", nameof(trigger));
        }

        var iterationId = Guid.NewGuid();
        var stepsTraversed = new List<int>();

        // Persist-before-publish (WP-027 R1 finding #4/#8's own established invariant): the
        // iteration row exists before LoopIterationStarted is ever published.
        await loopIterationStore.InsertAsync(
            new LoopIteration(iterationId, trigger.TriggerSource, entryStep, "Triggered", [], null, DateTimeOffset.UtcNow, null),
            cancellationToken);
        loopIterationStartedEventPublisher.PublishLoopIterationStarted(iterationId, trigger.TriggerSource, entryStep);

        string outcome;
        try
        {
            outcome = entryStep switch
            {
                1 => await RunObserveOnlyAsync(trigger, stepsTraversed, cancellationToken),
                2 => await RunFromUnderstandAsync(trigger, stepsTraversed, iterationId, cancellationToken),
                8 => await RunFromScheduleAsync(stepsTraversed, cancellationToken),
                11 => await RunObserveResultsOnlyAsync(trigger, stepsTraversed, cancellationToken),
                13 or 15 => RunLearningPhaseOnly(stepsTraversed, entryStep),
                _ => throw new InvalidOperationException($"No handling defined for entry step {entryStep}."),
            };
        }
        catch
        {
            await loopIterationStore.UpdateStateAsync(iterationId, "Failed", [.. stepsTraversed], cancellationToken);
            throw;
        }

        await loopIterationStore.CompleteAsync(iterationId, outcome, [.. stepsTraversed], cancellationToken);
        loopIterationCompletedEventPublisher.PublishLoopIterationCompleted(iterationId, [.. stepsTraversed], outcome);
    }

    public async Task<LoopStatus> GetCurrentStatusAsync(CancellationToken cancellationToken = default)
    {
        var latest = await loopIterationStore.GetLatestAsync(cancellationToken);
        // WP-028 Decision 2: CurrentMode is always Assisted (mode switching is WP-029);
        // LoopHealthScore is always null (Self-Evaluate is WP-029).
        return new LoopStatus(latest?.IterationId, OperationalMode.Assisted, LoopHealthScore: null);
    }

    /// <summary>
    /// Steps 2 (Understand) + 3 (Retrieve Context) + 4 (Reason) + 5 (Generate Alternatives) +
    /// 6 (Evaluate Risks) — all folded into one <see cref="IReasoningEngineClient.ReasonAsync"/>
    /// call (§7.1: the spec's step split is conceptual, not four separate invocations; §10:
    /// "entirely a sequencing of Reasoning Engine's own pipeline"), then step 7 (Validate), the
    /// mandatory Protection gate.
    /// </summary>
    private async Task<string> RunFromUnderstandAsync(
        TriggerContext trigger, List<int> stepsTraversed, Guid iterationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(trigger.SourcePayloadRef))
        {
            throw new ArgumentException(
                "UserRequest/ManualRequest triggers require SourcePayloadRef to carry the Goal statement.", nameof(trigger));
        }

        stepsTraversed.Add(2);
        var reasoningRequest = new ReasoningRequest(
            RequestId: Guid.NewGuid(),
            CorrelationId: iterationId,
            Goal: trigger.SourcePayloadRef,
            RequestingRole: trigger.TriggerSource);
        stepsTraversed.Add(3); // Retrieve Context — see class doc comment: folded into ReasonAsync, no separate call.
        var decisions = await reasoningEngineClient.ReasonAsync(reasoningRequest, cancellationToken);
        stepsTraversed.Add(4);
        stepsTraversed.Add(5); // Decision.RejectedHypotheses already carries this.
        stepsTraversed.Add(6); // Decision.RiskScore already carries this.

        var decision = decisions.FirstOrDefault()
            ?? throw new InvalidOperationException("ReasonAsync returned no Decision.");
        await loopIterationStore.UpdateStateAsync(iterationId, "Deciding", [.. stepsTraversed], cancellationToken);

        stepsTraversed.Add(7);
        var validation = protectionClient.Validate(new ActionRequest(
            ActionId: Guid.NewGuid(),
            ActionType: "LoopIterationDecision",
            Actor: trigger.TriggerSource,
            RiskScore: (int)Math.Round(decision.RiskScore)));

        // Protection Invariant: only Allow may proceed to step 8. Deny/Defer/Retry is a
        // legitimate completed iteration outcome, never treated as an implementation failure.
        if (validation.Verdict != ProtectionVerdict.Allow)
        {
            return "Denied";
        }

        await loopIterationStore.UpdateStateAsync(iterationId, "Executing", [.. stepsTraversed], cancellationToken);
        return await SubmitAndExecuteAsync(trigger, stepsTraversed, cancellationToken);
    }

    /// <summary>Step 8 (Plan) + steps 9-12 via <see cref="RunFromScheduleAsync"/> + Learning phase.</summary>
    private async Task<string> SubmitAndExecuteAsync(TriggerContext trigger, List<int> stepsTraversed, CancellationToken cancellationToken)
    {
        stepsTraversed.Add(8);
        var goal = new Goal(
            GoalId: Guid.NewGuid(),
            Statement: trigger.SourcePayloadRef!,
            ParentGoalId: null,
            DomainTags: [],
            SubmittedByActor: trigger.TriggerSource,
            State: GoalLifecycleState.Proposed,
            PlanId: null);
        await planningClient.SubmitGoalAsync(goal, cancellationToken);

        return await RunFromScheduleAsync(stepsTraversed, cancellationToken);
    }

    /// <summary>
    /// Steps 9 (Schedule) + 10 (Execute) + 11 (Observe Results) + 12 (Measure Outcomes, see class
    /// doc comment) + the Learning phase (13-15). Shared by the User/Manual Request path (after
    /// step 8) and the Scheduled Task trigger (§8: "Enters Loop At: Step 8 (Plan) or later, if the
    /// task's plan is already known" — realized here as entering directly at step 9, never
    /// re-submitting a Goal).
    /// </summary>
    private async Task<string> RunFromScheduleAsync(List<int> stepsTraversed, CancellationToken cancellationToken)
    {
        // Step 9: Schedule. TaskCreated/PlannerGenerated have already reached the Scheduler
        // synchronously as a side effect of the existing WP-024 EventMediator wiring (Program.cs)
        // triggered by SubmitGoalAsync's own event publication — EvaluateReadinessAsync is the
        // one Scheduler action that chain does not already perform.
        stepsTraversed.Add(9);
        await scheduler.EvaluateReadinessAsync(cancellationToken);

        // Step 10: Execute — Execution Coordinator's own chokepoint (FR-PE1), independently
        // re-validating Protection internally (FR-PE2) — the Protection Invariant's second,
        // pre-existing layer, unmodified by this WP.
        stepsTraversed.Add(10);
        var dispatch = await executionCoordinator.DispatchNextAsync(cancellationToken);

        // Step 11: Observe Results.
        stepsTraversed.Add(11);
        if (dispatch.Task is not null)
        {
            await progressMonitor.GetGoalProgressAsync(dispatch.Task.GoalId, cancellationToken);
        }

        // Step 12: Measure Outcomes — see class doc comment. Recorded as traversed structurally
        // only; no citable capability exists in this repository.
        stepsTraversed.Add(12);

        return RunLearningPhaseOnly(stepsTraversed, startAt: 13);
    }

    /// <summary>Step 1 (Observe) only — Performance Degradation's realizable citation (Resource Management state).</summary>
    private Task<string> RunObserveOnlyAsync(TriggerContext trigger, List<int> stepsTraversed, CancellationToken cancellationToken)
    {
        stepsTraversed.Add(1);
        if (trigger.SourcePayloadRef is { } resourceTypeName && Enum.TryParse<ResourceType>(resourceTypeName, out var resourceType))
        {
            _ = resourceManagementClient.GetCurrentTier(resourceType);
        }

        return Task.FromResult("Completed");
    }

    /// <summary>
    /// Step 11 (Observe Results) only — the Failure trigger's realizable citation. A
    /// Failure-triggered iteration reaching "Completed" describes this iteration's own execution
    /// result (it ran to completion, recording that a failure was observed), not a judgment about
    /// the triggering condition itself.
    /// </summary>
    private async Task<string> RunObserveResultsOnlyAsync(TriggerContext trigger, List<int> stepsTraversed, CancellationToken cancellationToken)
    {
        stepsTraversed.Add(11);
        if (trigger.SourcePayloadRef is { } taskIdText && Guid.TryParse(taskIdText, out var taskId))
        {
            await progressMonitor.GetTaskProgressAsync(taskId, cancellationToken);
        }

        return "Completed";
    }

    /// <summary>
    /// Steps 13-15 (Learn/Update Memory/Promote Knowledge) — see class doc comment: recorded as
    /// traversed only, never directly invoked, since no synchronous Learning Engine client exists
    /// anywhere in this repository.
    /// </summary>
    private static string RunLearningPhaseOnly(List<int> stepsTraversed, int startAt)
    {
        for (var step = startAt; step <= 15; step++)
        {
            stepsTraversed.Add(step);
        }

        return "Completed";
    }
}
