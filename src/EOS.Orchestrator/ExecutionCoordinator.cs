using EOS.Contracts;

namespace EOS.Orchestrator;

/// <summary>
/// Planning-Execution-Engine-Specification-v1.0 §10.7: the single structural chokepoint through
/// which every Task dispatch passes (FR-PE1) — calls <see cref="IProtectionClient.Validate"/>
/// (FR-PE2) before any <see cref="DispatchedTask"/> transitions
/// <see cref="TaskLifecycleState.Ready"/> → <see cref="TaskLifecycleState.Running"/>
/// (Constitution Part 6 §6.2), never overriding a Deny (§25.1). This is the only path in this
/// codebase by which that transition occurs.
/// </summary>
public sealed class ExecutionCoordinator(
    Scheduler scheduler,
    DispatchedTaskStore store,
    IProtectionClient protectionClient,
    ITaskStartedEventPublisher taskStartedEventPublisher,
    ITaskExecutionClient taskExecutionClient,
    ITaskCompletedEventPublisher taskCompletedEventPublisher,
    ITaskBlockedEventPublisher taskBlockedEventPublisher,
    IUniversalGateClient universalGateClient)
{
    public async Task<DispatchResult> DispatchNextAsync(CancellationToken cancellationToken = default)
    {
        var task = await scheduler.SelectNextDispatchableTaskAsync(cancellationToken);
        if (task is null)
        {
            return new DispatchResult(DispatchOutcome.NoEligibleTask, null);
        }

        // Constitution Part 6 §6.2's "Ready → Running" Actor is "Scheduler assigns; Role
        // executes" — no role-assignment mechanism exists anywhere in this codebase yet (no
        // competency-to-role mapping), so the Scheduler itself, the subsystem requesting this
        // dispatch, is the disclosed Actor — matching CompressionSweep's identical precedent
        // (Program.cs) of a fixed, disclosed Actor value where no real one is derivable yet.
        //
        // Known limitation, not fixed: EOS.Contracts.ActionRequest (frozen since WP-013, reused
        // identically by every IProtectionClient caller in this codebase — GoalValidator,
        // CompressionSweep, AskCommand) has no field for the specific entity an action concerns,
        // only a freshly-generated ActionId per call. This dispatch's ActionId is therefore not
        // task.TaskId itself; correlating a Protection log entry back to a specific Task requires
        // timestamp/context correlation, not a direct foreign key. Not fixed here — extending
        // ActionRequest's shape would be a breaking change to an already-frozen, widely-reused
        // contract spanning multiple already-merged Work Packages, well beyond WP-024's own scope.
        var validation = protectionClient.Validate(new ActionRequest(
            ActionId: Guid.NewGuid(),
            ActionType: "TaskDispatch",
            Actor: "Scheduler",
            RiskScore: 10));

        if (validation.Verdict != ProtectionVerdict.Allow)
        {
            // §7.3 step 5: "on failure to satisfy any check, task remains Ready and is
            // re-evaluated next micro-cycle" — the Task is left untouched, never overriding the
            // Deny (§25.1), never retried without a fresh validation pass.
            return new DispatchResult(DispatchOutcome.ProtectionDenied, task);
        }

        var running = task with { State = TaskLifecycleState.Running, RunningAt = DateTimeOffset.UtcNow };
        await store.UpsertAsync(running, cancellationToken);
        taskStartedEventPublisher.PublishTaskStarted(running.TaskId);

        return new DispatchResult(DispatchOutcome.Dispatched, running);
    }

    /// <summary>
    /// Post-Roadmap WP-A: the "Role executes" half of Constitution Part 6 §6.2's
    /// <c>Ready → Running</c> row and the actor-gated <c>Running → Review</c> row ("Actor role;
    /// Implementation evidence (diff, artifact)"), sequenced by this chokepoint (Planning-
    /// Execution-Engine-Specification-v1.0 §10.7, §22.4: an execution attempt only triggers the
    /// Constitutional transition once Protection returns Allow). The role produces and registers
    /// the evidence via <see cref="ITaskExecutionClient"/>; this method then validates the
    /// <c>TaskCompletion</c> action through <see cref="IProtectionClient.Validate"/> and persists
    /// <c>Review</c> before publishing <c>TaskCompleted</c> (persist-then-publish, exactly as
    /// <see cref="DispatchNextAsync"/> does for <c>TaskStarted</c>).
    ///
    /// Every non-success outcome — Protection denial, invalid/absent evidence, reasoning,
    /// workspace or registration failure, and in-process cancellation — is persisted as §6.2's
    /// existing <c>Running → Blocked</c> transition ("EOS.Gates, any role"; evidence: the
    /// <see cref="DispatchedTask.BlockedReason"/> root-cause note) and published as
    /// <c>TaskBlocked</c>, so a dispatched Task never remains <c>Running</c> and the Scheduler's
    /// concurrency slot (which counts <c>Running</c> only) is released in the same call that
    /// consumed it. <c>Blocked → Retry</c> is the unwired <see cref="RetryManager"/> path — not
    /// invoked here. A process crash mid-execution can still leave <c>Running</c>; recovery is
    /// explicitly deferred (§15.8/§19).
    ///
    /// ADR-009: Constitution Part 6 §6.2 requires Universal Gates §0.8.1 for <c>Running → Review</c>.
    /// After the registered evidence exists and before the <c>TaskCompletion</c> validation, Gates 1
    /// (build/static analysis) and 2 (unit tests) are executed through
    /// <see cref="IUniversalGateClient"/> against an isolated copy of the workspace — never the real
    /// working tree — and the pass/fail decision is the Rule Engine's (Protection §10.3). A failed
    /// gate is §0.8.3's blocking status: <c>Running → Blocked</c>, <c>TaskBlocked</c>, and no
    /// <c>TaskCompleted</c> (§0.15.1: <c>TaskCompleted</c> must reference passing evidence). A gate
    /// that cannot run is a failure, never a pass. Gates 3–5 remain a disclosed, unimplemented
    /// limitation — the same class of disclosure as <c>ProtectionGate</c>'s High-tier steps 2–4.
    /// </summary>
    public async Task<ExecutionResult> ExecuteAndCompleteAsync(DispatchedTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.State != TaskLifecycleState.Running)
        {
            throw new InvalidOperationException(
                $"Task '{task.TaskId}' is {task.State}, not Running — only a dispatched Task can be executed and completed.");
        }

        TaskExecutionResult execution;
        try
        {
            execution = await taskExecutionClient.ExecuteAsync(task, cancellationToken);
        }
        catch (OperationCanceledException cancellation) when (cancellationToken.IsCancellationRequested)
        {
            // In-process cancellation: the lifecycle write and its event must still complete —
            // with a non-cancelled token, since the supplied one is already cancelled — before the
            // cancellation is rethrown unchanged (LoopController.PersistFailedTerminalStateAsync's
            // established compensating-write pattern). If that compensating write itself fails,
            // neither exception is swallowed.
            try
            {
                await BlockAsync(task, "Execution cancelled before completion.");
            }
            catch (Exception persistFailure)
            {
                throw new AggregateException(cancellation, persistFailure);
            }

            throw;
        }
        catch (Exception ex)
        {
            var blocked = await BlockAsync(task, $"Execution failed: {ex.Message}", cancellationToken);
            return new ExecutionResult(ExecutionOutcome.ExecutionFailed, blocked, [], ex.Message);
        }

        if (execution.EvidenceRefs.Length == 0)
        {
            var blocked = await BlockAsync(task, "Execution failed: the executing role returned no evidence reference.", cancellationToken);
            return new ExecutionResult(ExecutionOutcome.ExecutionFailed, blocked, [], "The executing role returned no evidence reference.");
        }

        UniversalGateDecision gates;
        try
        {
            gates = await universalGateClient.EvaluateAsync(task, execution.EvidenceRefs, cancellationToken);
        }
        catch (OperationCanceledException cancellation) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                await BlockAsync(task, "Execution cancelled during Universal Gate evaluation.");
            }
            catch (Exception persistFailure)
            {
                throw new AggregateException(cancellation, persistFailure);
            }

            throw;
        }
        catch (Exception ex)
        {
            // Fail closed: a gate that could not be evaluated has not passed (§0.8.3).
            var reason = $"Universal Gate evaluation failed: {ex.Message}. Evidence: {string.Join(", ", execution.EvidenceRefs)}";
            var blocked = await BlockAsync(task, reason, cancellationToken);
            return new ExecutionResult(ExecutionOutcome.ExecutionFailed, blocked, execution.EvidenceRefs, reason);
        }

        if (!gates.Passed)
        {
            // §6.2 Running → Blocked, actor EOS.Gates, evidence "Gate failure record" — the Rule
            // Engine's reason names the failing gate; the registered artifact stays immutable.
            var reason = $"Universal Gate failure: {gates.FailureReason ?? "no reason supplied"}. Evidence: {string.Join(", ", execution.EvidenceRefs)}";
            var blocked = await BlockAsync(task, reason, cancellationToken);
            return new ExecutionResult(ExecutionOutcome.ExecutionFailed, blocked, execution.EvidenceRefs, reason);
        }

        // Same known limitation as DispatchNextAsync's TaskDispatch validation: the frozen
        // ActionRequest carries no field for the specific Task, so correlation is by log context.
        var validation = protectionClient.Validate(new ActionRequest(
            ActionId: Guid.NewGuid(),
            ActionType: "TaskCompletion",
            Actor: "SeniorEngineer",
            RiskScore: 10));

        if (validation.Verdict != ProtectionVerdict.Allow)
        {
            // §6.2 Running → Blocked, actor EOS.Gates, evidence "Gate failure record": the
            // registered artifact stays immutable and is cited in the root-cause note; no Review
            // transition and no TaskCompleted are ever produced for a denied completion.
            var reason = $"TaskCompletion denied: {validation.Verdict} — {validation.Reason ?? "no reason supplied"}. Evidence: {string.Join(", ", execution.EvidenceRefs)}";
            var blocked = await BlockAsync(task, reason, cancellationToken);
            return new ExecutionResult(ExecutionOutcome.ProtectionDenied, blocked, execution.EvidenceRefs, reason);
        }

        var review = task with { State = TaskLifecycleState.Review };
        await store.UpsertAsync(review, cancellationToken);
        taskCompletedEventPublisher.PublishTaskCompleted(review.TaskId, execution.EvidenceRefs);

        return new ExecutionResult(ExecutionOutcome.Completed, review, execution.EvidenceRefs, null);
    }

    private async Task<DispatchedTask> BlockAsync(DispatchedTask task, string reason, CancellationToken cancellationToken = default)
    {
        var blocked = task with { State = TaskLifecycleState.Blocked, BlockedReason = reason };
        await store.UpsertAsync(blocked, cancellationToken);
        taskBlockedEventPublisher.PublishTaskBlocked(blocked.TaskId, reason);
        return blocked;
    }
}
