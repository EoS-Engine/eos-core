using EOS.Contracts;
using EOS.Gates;
using Microsoft.Extensions.Logging;

namespace EOS.Gates.Tests;

public class ProtectionGateTests
{
    private static ActionRequest CreateRequest(int riskScore, string actionType = "TestAction")
    {
        return new ActionRequest(
            ActionId: Guid.NewGuid(),
            ActionType: actionType,
            Actor: "EOS.Reasoning",
            RiskScore: riskScore);
    }

    private static readonly ResourceCeilings DefaultResourceCeilings = new(
        CpuCeilingPercent: 90,
        RamCeilingMegabytes: 8192,
        DiskCeilingMegabytes: 476000,
        ModelUsageCeilingTokens: 100000,
        ContextSizeCeilingTokens: 32000,
        BackgroundTasksCeilingCount: 4);

    private static ProtectionGate CreateGate(ILogger<ProtectionGate>? logger = null)
    {
        return new ProtectionGate(
            new PolicyEngine([], [], [], []),
            new RuleEngine(),
            new RiskEngine(),
            new ApprovalEngine(),
            new EmergencyShutdownState(),
            DefaultResourceCeilings,
            logger ?? new RecordingLogger());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(30)]
    public void Validate_ReturnsAllow_ForLowTierAction(int riskScore)
    {
        var gate = CreateGate();
        var result = gate.Validate(CreateRequest(riskScore));

        Assert.Equal(ProtectionVerdict.Allow, result.Verdict);
        Assert.Equal(RiskTier.Low, result.Tier);
        Assert.Null(result.Reason);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(50)]
    [InlineData(70)]
    public void Validate_ReturnsAllow_ForMediumTierAction_WhenNoPolicyDenies(int riskScore)
    {
        var gate = CreateGate();
        var result = gate.Validate(CreateRequest(riskScore));

        Assert.Equal(ProtectionVerdict.Allow, result.Verdict);
        Assert.Equal(RiskTier.Medium, result.Tier);
    }

    [Theory]
    [InlineData(71)]
    [InlineData(85)]
    [InlineData(100)]
    public void Validate_ReturnsAllow_ForHighTierAction_WhenPipelineClearsAndNoHumanSignOffIsRequired(int riskScore)
    {
        var gate = CreateGate();
        var result = gate.Validate(CreateRequest(riskScore));

        Assert.Equal(ProtectionVerdict.Allow, result.Verdict);
        Assert.Equal(RiskTier.High, result.Tier);
    }

    [Fact]
    public void Validate_DefersForApproval_WhenHighTierActionTypeRequiresHumanSignOff()
    {
        var gate = CreateGate();
        var result = gate.Validate(CreateRequest(riskScore: 100, actionType: "Security-sensitive change"));

        Assert.Equal(ProtectionVerdict.Defer, result.Verdict);
        Assert.Equal(RiskTier.High, result.Tier);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void Validate_DeniesMediumTierAction_WhenAGlobalPolicyDenies()
    {
        var policyEngine = new PolicyEngine(
            globalPolicies: [new PolicyEntry("TestAction", "Deny", "Denied by test global policy.")],
            projectPolicies: [],
            userPolicies: [],
            runtimePolicies: []);
        var gate = new ProtectionGate(
            policyEngine, new RuleEngine(), new RiskEngine(), new ApprovalEngine(),
            new EmergencyShutdownState(), DefaultResourceCeilings, new RecordingLogger());

        var result = gate.Validate(CreateRequest(riskScore: 50));

        Assert.Equal(ProtectionVerdict.Deny, result.Verdict);
        Assert.Equal(RiskTier.Medium, result.Tier);
        Assert.Equal("Denied by test global policy.", result.Reason);
    }

    [Fact]
    public void Validate_EscalatesToHighTier_AfterTwoConsecutiveMediumTierDenials()
    {
        var policyEngine = new PolicyEngine(
            globalPolicies: [new PolicyEntry("TestAction", "Deny", "Denied by test global policy.")],
            projectPolicies: [],
            userPolicies: [],
            runtimePolicies: []);
        var gate = new ProtectionGate(
            policyEngine, new RuleEngine(), new RiskEngine(), new ApprovalEngine(),
            new EmergencyShutdownState(), DefaultResourceCeilings, new RecordingLogger());
        var request = CreateRequest(riskScore: 50);

        gate.Validate(request);
        gate.Validate(request);
        var thirdResult = gate.Validate(request);

        Assert.Equal(RiskTier.High, thirdResult.Tier);
    }

    [Fact]
    public void Validate_LogsTheDecision()
    {
        var logger = new RecordingLogger();
        var gate = CreateGate(logger);

        var request = CreateRequest(riskScore: 10);
        gate.Validate(request);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains($"ActionId={request.ActionId}", entry.Message);
        Assert.Contains($"ActionType={request.ActionType}", entry.Message);
        Assert.Contains($"Actor={request.Actor}", entry.Message);
        Assert.Contains($"RiskScore={request.RiskScore}", entry.Message);
        Assert.Contains($"Tier={RiskTier.Low}", entry.Message);
        Assert.Contains($"Verdict={ProtectionVerdict.Allow}", entry.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validate_ReturnsDeny_ForOutOfRangeRiskScore(int riskScore)
    {
        var gate = CreateGate();
        var result = gate.Validate(CreateRequest(riskScore));

        Assert.Equal(ProtectionVerdict.Deny, result.Verdict);
        Assert.NotNull(result.Reason);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(50)]
    public void Validate_ReturnsDeny_ForBlankActionType_RegardlessOfTier(int riskScore)
    {
        var gate = CreateGate();
        var result = gate.Validate(CreateRequest(riskScore, actionType: "   "));

        Assert.Equal(ProtectionVerdict.Deny, result.Verdict);
        Assert.NotNull(result.Reason);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(50)]
    public void Validate_ReturnsDeny_ForBlankActor_RegardlessOfTier(int riskScore)
    {
        var gate = CreateGate();
        var request = new ActionRequest(Guid.NewGuid(), "TestAction", Actor: " ", riskScore);

        var result = gate.Validate(request);

        Assert.Equal(ProtectionVerdict.Deny, result.Verdict);
        Assert.NotNull(result.Reason);
    }

    private sealed class RecordingLogger : ILogger<ProtectionGate>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
