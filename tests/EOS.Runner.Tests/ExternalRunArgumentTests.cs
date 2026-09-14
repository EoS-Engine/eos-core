namespace EOS.Runner.Tests;

public class ExternalRunArgumentTests
{
    [Fact]
    public void Parse_PreservesLegacySelfRepositoryRun()
    {
        var parsed = RunCommandArguments.Parse(["run", "Add a health endpoint"]);

        Assert.Equal(RunCommandKind.LegacySelfRepository, parsed.Kind);
        Assert.Equal("Add a health endpoint", parsed.TaskText);
        Assert.Null(parsed.TargetPath);
        Assert.False(parsed.TrustBuildTest);
    }

    [Fact]
    public void Parse_AcceptsExternalRun_WithExplicitTargetAndTrust()
    {
        var parsed = RunCommandArguments.Parse(["run", "--target", "/tmp/project", "--trust-build-test", "Change src/App.cs"]);

        Assert.Equal(RunCommandKind.ExternalTarget, parsed.Kind);
        Assert.Equal("/tmp/project", parsed.TargetPath);
        Assert.Equal("Change src/App.cs", parsed.TaskText);
        Assert.True(parsed.TrustBuildTest);
        Assert.Null(parsed.Error);
    }

    [Fact]
    public void Parse_RejectsExternalTargetWithoutTrust()
    {
        var parsed = RunCommandArguments.Parse(["run", "--target", "/tmp/project", "Change src/App.cs"]);

        Assert.Equal(RunCommandKind.Malformed, parsed.Kind);
        Assert.Contains("--trust-build-test", parsed.Error);
    }

    [Theory]
    [InlineData("run", "--target")]
    [InlineData("run", "--target", "/tmp/project")]
    [InlineData("run", "--trust-build-test", "Change src/App.cs")]
    [InlineData("run", "--target", "/tmp/project", "--trust-build-test")]
    [InlineData("run", "--target", "", "--trust-build-test", "Change src/App.cs")]
    [InlineData("run", "--target", "/tmp/project", "--trust-build-test", "")]
    public void Parse_RejectsMalformedExternalRun(params string[] args)
    {
        var parsed = RunCommandArguments.Parse(args);

        Assert.Equal(RunCommandKind.Malformed, parsed.Kind);
        Assert.NotNull(parsed.Error);
    }

    [Fact]
    public void DecideContinuation_FailsMalformedExternalCommand()
    {
        var parsed = RunCommandArguments.Parse(["run", "--target", "/tmp/project"]);

        var decision = RunCommandArguments.DecideContinuation(parsed);

        Assert.Equal(RunCommandExecutionRoute.Fail, decision.Route);
    }

    [Fact]
    public void DecideContinuation_FailsExternalCommandWithoutTrust()
    {
        var parsed = new RunCommandArguments(
            RunCommandKind.ExternalTarget,
            TaskText: "Change src/App.cs",
            TargetPath: "/tmp/project",
            TrustBuildTest: false,
            Error: null);

        var decision = RunCommandArguments.DecideContinuation(parsed);

        Assert.Equal(RunCommandExecutionRoute.Fail, decision.Route);
        Assert.Contains("--trust-build-test", decision.Error);
    }

    [Fact]
    public void DecideContinuation_FailsExternalCommandWhenPreflightFails()
    {
        var parsed = RunCommandArguments.Parse(["run", "--target", "/tmp/project", "--trust-build-test", "Change src/App.cs"]);

        var decision = RunCommandArguments.DecideContinuation(parsed, preflightFailure: new InvalidOperationException("not git"));

        Assert.Equal(RunCommandExecutionRoute.Fail, decision.Route);
        Assert.Equal("not git", decision.Error);
    }

    [Fact]
    public void DecideContinuation_StopsAfterSuccessfulS1ExternalPreflight()
    {
        var parsed = RunCommandArguments.Parse(["run", "--target", "/tmp/project", "--trust-build-test", "Change src/App.cs"]);

        var decision = RunCommandArguments.DecideContinuation(parsed, preflightSucceeded: true);

        Assert.Equal(RunCommandExecutionRoute.StopAfterS1Preflight, decision.Route);
    }

    [Fact]
    public void DecideContinuation_AllowsLegacySelfRepositoryRunToReachExistingContinuation()
    {
        var parsed = RunCommandArguments.Parse(["run", "Add a health endpoint"]);

        var decision = RunCommandArguments.DecideContinuation(parsed);

        Assert.Equal(RunCommandExecutionRoute.ContinueLegacy, decision.Route);
    }
}
