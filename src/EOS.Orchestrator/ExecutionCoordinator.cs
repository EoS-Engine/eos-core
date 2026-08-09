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
    ITaskStartedEventPublisher taskStartedEventPublisher)
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
}
