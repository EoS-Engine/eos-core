using EOS.Contracts;
using EOS.Gates;

namespace EOS.Gates.Tests;

public class EmergencyShutdownTests
{
    private static ActionRequest CreateRequest(string actionType, int riskScore = 10, string actor = "EOS.Reasoning")
    {
        return new ActionRequest(Guid.NewGuid(), actionType, actor, riskScore);
    }

    [Fact]
    public void TryHandleControlAction_ReturnsNull_ForAnUnrelatedAction()
    {
        var state = new EmergencyShutdownState();

        var result = state.TryHandleControlAction(CreateRequest("TestAction"));

        Assert.Null(result);
    }

    [Fact]
    public void TryHandleControlAction_Activates_AndReturnsAllow()
    {
        var state = new EmergencyShutdownState();

        var result = state.TryHandleControlAction(CreateRequest("EmergencyShutdown", actor: "CTO"));

        Assert.NotNull(result);
        Assert.Equal(ProtectionVerdict.Allow, result.Verdict);
        Assert.Contains("CTO", result.Reason);
    }

    [Fact]
    public void TryHandleControlAction_HoldsSubsequentActions_AtDefer_WhileActive()
    {
        var state = new EmergencyShutdownState();
        state.TryHandleControlAction(CreateRequest("EmergencyShutdown"));

        var result = state.TryHandleControlAction(CreateRequest("TestAction"));

        Assert.NotNull(result);
        Assert.Equal(ProtectionVerdict.Defer, result.Verdict);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void TryHandleControlAction_Clears_AndSubsequentActionsResumeNormalDispatch()
    {
        var state = new EmergencyShutdownState();
        state.TryHandleControlAction(CreateRequest("EmergencyShutdown"));

        var clearResult = state.TryHandleControlAction(CreateRequest("EmergencyShutdownCleared", actor: "CTO"));
        var resumedResult = state.TryHandleControlAction(CreateRequest("TestAction"));

        Assert.NotNull(clearResult);
        Assert.Equal(ProtectionVerdict.Allow, clearResult.Verdict);
        Assert.Null(resumedResult);
    }

    [Fact]
    public void TryHandleControlAction_IsCaseInsensitive()
    {
        var state = new EmergencyShutdownState();

        var result = state.TryHandleControlAction(CreateRequest("emergencyshutdown"));

        Assert.NotNull(result);
        Assert.Equal(ProtectionVerdict.Allow, result.Verdict);
    }

    [Fact]
    public void ProtectionGate_EndToEnd_ActivateHoldsDispatch_ClearResumesNormalOperation()
    {
        var policyEngine = new PolicyEngine([], [], [], []);
        var resourceCeilings = new ResourceCeilings(90, 8192, 476000, 100000, 32000, 4);
        var gate = new ProtectionGate(
            policyEngine, new RuleEngine(), new RiskEngine(), new ApprovalEngine(),
            new EmergencyShutdownState(), resourceCeilings, new NoOpLogger());

        var beforeShutdown = gate.Validate(CreateRequest("TestAction", riskScore: 10));
        Assert.Equal(ProtectionVerdict.Allow, beforeShutdown.Verdict);

        var activation = gate.Validate(CreateRequest("EmergencyShutdown", actor: "CTO"));
        Assert.Equal(ProtectionVerdict.Allow, activation.Verdict);

        var duringShutdownLow = gate.Validate(CreateRequest("TestAction", riskScore: 10));
        var duringShutdownMedium = gate.Validate(CreateRequest("TestAction", riskScore: 50));
        var duringShutdownHigh = gate.Validate(CreateRequest("TestAction", riskScore: 90));
        Assert.Equal(ProtectionVerdict.Defer, duringShutdownLow.Verdict);
        Assert.Equal(ProtectionVerdict.Defer, duringShutdownMedium.Verdict);
        Assert.Equal(ProtectionVerdict.Defer, duringShutdownHigh.Verdict);

        var clearing = gate.Validate(CreateRequest("EmergencyShutdownCleared", actor: "CTO"));
        Assert.Equal(ProtectionVerdict.Allow, clearing.Verdict);

        var afterClearing = gate.Validate(CreateRequest("TestAction", riskScore: 10));
        Assert.Equal(ProtectionVerdict.Allow, afterClearing.Verdict);
    }

    private sealed class NoOpLogger : Microsoft.Extensions.Logging.ILogger<ProtectionGate>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => false;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
