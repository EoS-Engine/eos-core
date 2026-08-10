namespace EOS.Contracts;

/// <summary>
/// Protection-Layer-Specification-v1.0 §20/§21's <c>RollbackRequested</c> event — Protection's
/// post-hoc verdict that an already-executed action was later found to have been wrongly allowed,
/// directed at the owning subsystem to execute the actual rollback (FR-P10: "Protection never
/// mutates content directly"). Defined here in EOS.Contracts, not Program.cs's usual
/// internal-record event-payload convention, because unlike this codebase's other events this one
/// genuinely needs to be referenced by both its future producer (EOS.Gates) and its consumer
/// (EOS.Orchestrator's Rollback Manager) across the Composition Root boundary — mirroring
/// <see cref="ActionRequest"/>'s own existing EOS.Contracts placement for the identical reason.
/// </summary>
public sealed record RollbackRequestedPayload(Guid ActionId, string OwningSubsystem, string Reason);
