using EOS.Contracts;
using EOS.Gates;
using Microsoft.Extensions.Logging.Abstractions;

namespace EOS.Runner.Tests;

/// <summary>
/// Proves that the exact <see cref="ActionRequest"/> shape <c>Program.cs</c>'s <c>"compress"</c>
/// verb constructs (<c>ActionType: "MemoryCompression"</c>, <c>RiskScore: 10</c>) is real
/// Protection-Layer-gated dispatch, not a simplified path — both the Allow outcome and a real
/// deny/hold outcome, matching the same real-<c>ProtectionGate</c>-construction pattern
/// <c>ProtectionGateTests.CreateGate</c> and <c>ProtectionDomainPolicyTests</c> already use.
/// Exercises <see cref="ProtectionGate"/> directly (the same class <c>Program.cs</c> constructs
/// and calls) rather than the top-level <c>Program.cs</c> statements themselves, since those
/// are not extracted into a separate, directly-invokable command class this WP.
///
/// <b>Finding surfaced while writing this test:</b> at <c>RiskScore: 10</c>, the compress
/// action resolves to <see cref="RiskTier.Low"/> (Protection-Layer-Specification-v1.0 §14.1),
/// whose Low-tier path (<c>ProtectionGate.ValidateLowTier</c>) is unconditionally Allow —
/// "async-log only, never blocks (FR-P1)" — regardless of any configured <c>PolicyEngine</c>
/// Deny entry for <c>"MemoryCompression"</c>. A true <see cref="ProtectionVerdict.Deny"/> via
/// policy is therefore structurally impossible for this action at its current RiskScore. The
/// one real mechanism that can still hold this specific action is Emergency Shutdown
/// (<see cref="EmergencyShutdownState"/>), checked before tier dispatch for every ActionType
/// regardless of tier — verified below as the genuine Deny-equivalent (<see
/// cref="ProtectionVerdict.Defer"/>) case, rather than asserting a policy-Deny outcome that
/// cannot actually occur in the shipped configuration.
/// </summary>
public class CompressProtectionGateTests
{
    private static readonly ResourceCeilings DefaultResourceCeilings = new(
        CpuCeilingPercent: 90,
        RamCeilingMegabytes: 8192,
        DiskCeilingMegabytes: 476000,
        ModelUsageCeilingTokens: 100000,
        ContextSizeCeilingTokens: 32000,
        BackgroundTasksCeilingCount: 4);

    private static ActionRequest CreateCompressionActionRequest() => new(
        ActionId: Guid.NewGuid(), ActionType: "MemoryCompression", Actor: "HumanOperator", RiskScore: 10);

    private static ProtectionGate CreateGate(EmergencyShutdownState? emergencyShutdownState = null)
    {
        return new ProtectionGate(
            new PolicyEngine([], [], [], []),
            new RuleEngine(),
            new RiskEngine(),
            new ApprovalEngine(),
            emergencyShutdownState ?? new EmergencyShutdownState(),
            DefaultResourceCeilings,
            new StubResourceManagementClient(),
            NullLogger<ProtectionGate>.Instance);
    }

    private sealed class StubResourceManagementClient : IResourceManagementClient
    {
        public double GetCurrentBudget(ResourceType resourceType) => 0;

        public CapacityTier GetCurrentTier(ResourceType resourceType) => CapacityTier.Safe;
    }

    [Fact]
    public void Validate_ReturnsAllow_ForTheCompressActionRequest_UnderNormalOperation()
    {
        var gate = CreateGate();

        var result = gate.Validate(CreateCompressionActionRequest());

        Assert.Equal(ProtectionVerdict.Allow, result.Verdict);
    }

    [Fact]
    public void Validate_HoldsTheCompressActionRequestAtDefer_WhenEmergencyShutdownIsActive()
    {
        var emergencyShutdownState = new EmergencyShutdownState();
        emergencyShutdownState.TryHandleControlAction(
            new ActionRequest(Guid.NewGuid(), "EmergencyShutdown", "HumanOperator", RiskScore: 100));
        var gate = CreateGate(emergencyShutdownState);

        var result = gate.Validate(CreateCompressionActionRequest());

        Assert.Equal(ProtectionVerdict.Defer, result.Verdict);
        Assert.Equal("Emergency Shutdown is active; new dispatch is held pending clearance.", result.Reason);
    }
}
