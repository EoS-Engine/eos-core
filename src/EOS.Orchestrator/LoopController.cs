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
    ILoopIterationCompletedEventPublisher loopIterationCompletedEventPublisher,
    IOperationalModeStore operationalModeStore,
    IOperationalModeChangedEventPublisher operationalModeChangedEventPublisher,
    ILoopIterationEvaluatedEventPublisher loopIterationEvaluatedEventPublisher) : ILoopControlClient
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
                $"'{trigger.TriggerSource}' is not one of the 7 in-scope Trigger Sources (§8).", nameof(trigger));
        }

        var iterationId = Guid.NewGuid();
        var stepsTraversed = new List<int>();

        // Persist-before-publish (WP-027 R1 finding #4/#8's own established invariant): the
        // iteration row exists before LoopIterationStarted is ever published. If InsertAsync
        // itself fails, there is no row yet to compensate — nothing further to do here.
        await loopIterationStore.InsertAsync(
            new LoopIteration(iterationId, trigger.TriggerSource, entryStep, "Triggered", [], null, DateTimeOffset.UtcNow, null),
            cancellationToken);

        // Independent pre-review finding (post-R1): a LoopIterationStarted publish failure
        // previously left the row stuck at "Triggered" forever, with no compensating write at
        // all. Routed through the same compensating-Failed-write helper as step execution and
        // terminal-completion failures below.
        try
        {
            loopIterationStartedEventPublisher.PublishLoopIterationStarted(iterationId, trigger.TriggerSource, entryStep);
        }
        catch (Exception ex)
        {
            await PersistFailedTerminalStateAsync(iterationId, stepsTraversed, ex);
            throw;
        }

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
        catch (Exception ex)
        {
            await PersistFailedTerminalStateAsync(iterationId, stepsTraversed, ex);
            throw;
        }

        // Steps 16 (Self-Evaluate) + 17 (Improve) — WP-029, §13. Run once per successfully
        // completed iteration, independent of which entry-step path produced the completion
        // (§13.1: "on loop_iteration_complete(iteration)" has no entry-step precondition), and
        // regardless of whether outcome is "Completed" or "Denied" — a Denied iteration is still
        // a legitimate completed iteration (see the Protection Invariant comment above), never a
        // Failure Strategy (§23) case. Routed through the same compensating-Failed-write pattern
        // as every other failure point in this method.
        try
        {
            await RunSelfEvaluateAndImproveAsync(iterationId, stepsTraversed, cancellationToken);
        }
        catch (Exception ex)
        {
            await PersistFailedTerminalStateAsync(iterationId, stepsTraversed, ex);
            throw;
        }

        // Independent pre-review finding (post-R1): the successful terminal write was previously
        // unprotected — a failure here left the row stuck at its last non-terminal state
        // ("Executing" etc.) with no Failed marking. An already-successful iteration is never
        // turned into Failed by anything other than this specific write itself failing.
        try
        {
            await loopIterationStore.CompleteAsync(iterationId, "Completed", outcome, [.. stepsTraversed], cancellationToken);
        }
        catch (Exception ex)
        {
            await PersistFailedTerminalStateAsync(iterationId, stepsTraversed, ex);
            throw;
        }

        loopIterationCompletedEventPublisher.PublishLoopIterationCompleted(iterationId, [.. stepsTraversed], outcome);
    }

    /// <summary>
    /// Autonomous-Engineering-Loop-Specification-v1.0 §22.9 — every Operational Mode change is
    /// itself a Decision-Matrix-governed action, routed through the exact same
    /// <see cref="IProtectionClient.Validate"/> gate as any other action; the Loop never
    /// manufactures an <see cref="ProtectionVerdict.Allow"/> result itself (WP-029 Decision 4).
    /// On <c>Allow</c>, the new mode is persisted and <c>OperationalModeChanged</c> (§17) is
    /// published; on any other verdict, the current mode is left untouched. If persistence
    /// succeeds but publication fails, the persisted mode remains authoritative (never rolled
    /// back) and the publication failure propagates unchanged to the caller.
    ///
    /// CodeRabbit pre-merge P1 finding #1 fix: <c>RiskScore: 100</c> is used, not an arbitrary
    /// low value — verified directly against <c>EOS.Gates.RiskEngine.ClassifyTier</c>'s own real,
    /// frozen thresholds (<c>LowTierMaxRiskScore = 30</c>, <c>MediumTierMaxRiskScore = 70</c>):
    /// any score in (70, 100] resolves to <c>RiskTier.High</c>, and
    /// <c>EOS.Gates.ProtectionGate.ValidateHighTier</c> is the only tier that invokes
    /// <c>ApprovalEngine.Resolve</c> (the Decision Matrix, Constitution §0.6/§10.4) —
    /// <c>ValidateLowTier</c> "is async-log only, never blocks" (that class's own comment), which
    /// would silently defeat §22.9's "a mode change is itself a Decision-Matrix-governed action"
    /// requirement against the real Protection implementation despite technically calling
    /// <c>Validate</c>. This is a single fixed constant, not a per-mode risk calculation, a
    /// <c>RuntimePolicy</c>, or a <c>PolicyProfile</c> — <c>EOS.Gates</c> itself is untouched.
    /// </summary>
    public async Task<ValidationResult> SetOperationalModeAsync(
        OperationalMode mode, string requestedBy, CancellationToken cancellationToken = default)
    {
        var validation = protectionClient.Validate(new ActionRequest(
            ActionId: Guid.NewGuid(),
            ActionType: "SetOperationalMode",
            Actor: requestedBy,
            RiskScore: 100));

        if (validation.Verdict != ProtectionVerdict.Allow)
        {
            return validation;
        }

        // CodeRabbit pre-merge P1 finding #2 fix: the previous mode is now obtained atomically
        // from the same write that persists the new one (IOperationalModeStore.SetCurrentModeAsync's
        // own doc comment) — no separate GetCurrentModeAsync call precedes this, which under
        // genuine concurrent SetOperationalModeAsync calls could observe a stale value by the time
        // it is used to construct OperationalModeChanged's from_mode.
        var fromMode = await operationalModeStore.SetCurrentModeAsync(mode, cancellationToken);
        operationalModeChangedEventPublisher.PublishOperationalModeChanged(fromMode, mode, requestedBy);

        return validation;
    }

    /// <summary>
    /// Autonomous-Engineering-Loop-Specification-v1.0 §14.4 — identical to Protection Layer's own
    /// Emergency Shutdown (Protection-Layer-Specification-v1.0 §26.1); delegates entirely to the
    /// existing <c>EmergencyShutdownState</c> mechanism via the same <see cref="IProtectionClient.Validate"/>
    /// gate every other action already uses — no second, competing emergency-stop implementation
    /// exists in this class (WP-029 Decision 4). <paramref name="reason"/> mirrors the frozen
    /// pseudocode's <c>emergency_stop(requested_by, reason)</c> signature for interface fidelity;
    /// the existing, unmodified <see cref="ActionRequest"/> has no field to carry it, so it is not
    /// threaded into the Protection call — a disclosed limitation, not a silent drop.
    /// </summary>
    public Task<ValidationResult> EmergencyStopAsync(
        string requestedBy, string reason, CancellationToken cancellationToken = default)
    {
        _ = reason;
        var validation = protectionClient.Validate(new ActionRequest(
            ActionId: Guid.NewGuid(),
            ActionType: "EmergencyShutdown",
            Actor: requestedBy,
            RiskScore: 0));

        return Task.FromResult(validation);
    }

    /// <summary>
    /// CodeRabbit R1 finding #3's compensating-write pattern, shared by every failure point in
    /// <see cref="RunIterationAsync"/> (Started-publish failure, step-execution failure, and
    /// successful-terminal-persistence failure): a non-cancellable token, since the original
    /// failure may itself be the supplied token having been cancelled; the original exception is
    /// rethrown unchanged by the caller on success, or preserved alongside a persistence failure
    /// via <see cref="AggregateException"/> — never silently replaced.
    /// </summary>
    private async Task PersistFailedTerminalStateAsync(Guid iterationId, List<int> stepsTraversed, Exception originalException)
    {
        try
        {
            await loopIterationStore.CompleteAsync(iterationId, "Failed", "Failed", [.. stepsTraversed], CancellationToken.None);
        }
        catch (Exception persistFailure)
        {
            throw new AggregateException(originalException, persistFailure);
        }
    }

    public async Task<LoopStatus> GetCurrentStatusAsync(CancellationToken cancellationToken = default)
    {
        var latest = await loopIterationStore.GetLatestAsync(cancellationToken);
        var currentMode = await operationalModeStore.GetCurrentModeAsync(cancellationToken);
        // WP-029 Decision 1 (locked): LoopHealthScore is unconditionally null — no aggregation
        // formula exists for combining the five KPI families (§13.1), and none is invented here.
        return new LoopStatus(latest?.IterationId, currentMode, LoopHealthScore: null);
    }

    /// <summary>
    /// Steps 16 (Self-Evaluate) + 17 (Improve) — Autonomous-Engineering-Loop-Specification-v1.0
    /// §13. WP-029 Decision 1 (locked): none of the five KPI families §13.1 names has a queryable
    /// capability anywhere in this repository (verified directly, not assumed) — Goal Completion
    /// Rate/Execution Success Rate (Planning-Execution-Engine-Specification-v1.0 §28), Decision/
    /// Confidence Accuracy (Reasoning-Engine-Specification-v1.0 §25), Pipeline Throughput/Stall
    /// Rate (Learning-Engine-Specification-v1.1 §33), False Positive/Negative Rate (Protection-
    /// Layer-Specification-v1.0 §30), Resource Contention Rate (Resource-Management-Specification-
    /// v1.0 §28). No aggregation formula exists in any approved document for combining even a
    /// partially-available subset. <c>loop_health_score</c> is therefore unconditionally
    /// <c>null</c> — never a partial score, never an estimate, never a placeholder value —
    /// deferred to a future approved ADR/specification revision (WP-029 Decision 1).
    /// </summary>
    private async Task RunSelfEvaluateAndImproveAsync(Guid iterationId, List<int> stepsTraversed, CancellationToken cancellationToken)
    {
        // §19.1's Loop Iteration Lifecycle names Evaluating/Improving as the states between
        // Learning and Completed — WP-028 Decision 4 deliberately kept LoopIteration.State a
        // plain string (not an enum) specifically so WP-029 could write these exact values with
        // no type/schema change (final-adversarial-review finding: these transitions were
        // previously never written, even though stepsTraversed already recorded 16/17 — the
        // persisted state timeline silently skipped straight to Completed).
        await loopIterationStore.UpdateStateAsync(iterationId, "Evaluating", [.. stepsTraversed], cancellationToken);
        stepsTraversed.Add(16);
        loopIterationEvaluatedEventPublisher.PublishLoopIterationEvaluated(iterationId, loopHealthScore: null);

        // Step 17 (Improve, §13.2/§20.5): §20.5's own sequence diagram places submit_goal inside
        // an "alt sustained decline detected" guard — it is NOT called on every completed
        // iteration (final-adversarial-review correction; the prior unconditional call was a
        // misreading of §13.2's prose in isolation). "Planning-->>Loop: scheduled per each
        // subsystem's own Quarterly-cycle process" (§20.5) places the Quarterly calendar boundary
        // entirely inside Planning's own responsibility once a Goal is submitted — the Loop only
        // ever decides WHETHER to submit, never WHEN in calendar terms, and owns no cadence
        // tracking of its own.
        //
        // WP-029 Decision 1 (locked): loop_health_score is unconditionally null (no KPI
        // aggregation capability exists in this repository) — see PublishLoopIterationEvaluated
        // above. A "sustained decline" is a comparison over a history of measured scores; with
        // every measurement always null, no decline can ever be detected. sustainedDeclineDetected
        // is therefore always false today, and SubmitGoalAsync is correctly never called — not a
        // suppressed feature, not a workaround, but the literal, correct realization of §20.5's
        // guard given this repository's current, honestly-disclosed KPI unavailability. A future
        // WP that gives loop_health_score a real, non-null history can make this guard reachable
        // without this method's Quarterly-scheduling responsibility (owned by Planning, per §20.5)
        // ever changing.
        await loopIterationStore.UpdateStateAsync(iterationId, "Improving", [.. stepsTraversed], cancellationToken);
        stepsTraversed.Add(17);
        var sustainedDeclineDetected = false; // Always false: no loop_health_score history exists (D1) to detect a decline from.
        if (sustainedDeclineDetected)
        {
            await planningClient.SubmitGoalAsync(
                new Goal(
                    GoalId: Guid.NewGuid(),
                    Statement: "Quarterly recalibration/review (Autonomous-Engineering-Loop-Specification-v1.0 §13.2)",
                    ParentGoalId: null,
                    DomainTags: [],
                    SubmittedByActor: "AutonomousEngineeringLoop",
                    State: GoalLifecycleState.Proposed,
                    PlanId: null),
                cancellationToken);
        }
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
            RequestingRole: trigger.TriggerSource,
            // Step 3 (Retrieve Context): supplying any non-null ReasoningContextScope is what
            // actually triggers ReasoningEngine's own Context Assembly (ProcessContextAsync) —
            // an unset ContextScope is a documented no-op there. No filters are known at this
            // layer, so every field is left null; this is the minimal, honest way to invoke the
            // existing mechanism without inventing scope data the Loop doesn't have.
            ContextScope: new ReasoningContextScope(DomainTags: null, ProjectScope: null, Budget: null));
        stepsTraversed.Add(3); // Retrieve Context — folded into ReasonAsync via ContextScope above, no separate call.
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
