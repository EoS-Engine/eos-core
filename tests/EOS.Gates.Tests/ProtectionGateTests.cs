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

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(30)]
    public void Validate_ReturnsAllow_ForLowTierAction(int riskScore)
    {
        var gate = new ProtectionGate(new RecordingLogger());
        var result = gate.Validate(CreateRequest(riskScore));

        Assert.Equal(ProtectionVerdict.Allow, result.Verdict);
        Assert.Equal(RiskTier.Low, result.Tier);
        Assert.Null(result.Reason);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(50)]
    [InlineData(70)]
    public void Validate_ReturnsDeny_ForMediumTierAction(int riskScore)
    {
        var gate = new ProtectionGate(new RecordingLogger());
        var result = gate.Validate(CreateRequest(riskScore));

        Assert.Equal(ProtectionVerdict.Deny, result.Verdict);
        Assert.Equal(RiskTier.Medium, result.Tier);
        Assert.NotNull(result.Reason);
    }

    [Theory]
    [InlineData(71)]
    [InlineData(85)]
    [InlineData(100)]
    public void Validate_ReturnsDeny_ForHighTierAction(int riskScore)
    {
        var gate = new ProtectionGate(new RecordingLogger());
        var result = gate.Validate(CreateRequest(riskScore));

        Assert.Equal(ProtectionVerdict.Deny, result.Verdict);
        Assert.Equal(RiskTier.High, result.Tier);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void Validate_ADeliberatelyHighRiskTestAction_IsNotAutoAllowed()
    {
        var gate = new ProtectionGate(new RecordingLogger());
        var result = gate.Validate(CreateRequest(riskScore: 100, actionType: "DeliberatelyHighRiskTestAction"));

        Assert.NotEqual(ProtectionVerdict.Allow, result.Verdict);
    }

    [Fact]
    public void Validate_LogsTheDecision()
    {
        var logger = new RecordingLogger();
        var gate = new ProtectionGate(logger);

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
        var gate = new ProtectionGate(new RecordingLogger());
        var result = gate.Validate(CreateRequest(riskScore));

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
