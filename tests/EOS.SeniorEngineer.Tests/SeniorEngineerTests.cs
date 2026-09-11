using EOS.Contracts;
using EOS.SeniorEngineer;

namespace EOS.SeniorEngineer.Tests;

// Post-Roadmap WP-A: the role's execution semantics against hand-rolled doubles (this repository
// uses no mocking framework). The real Reasoning Engine / Ollama path is exercised by the runtime
// demo, never by these deterministic tests.
public class SeniorEngineerTests
{
    private const string SrcPath = "src/EOS.Web/DashboardWebHost.cs";
    private const string TestPath = "tests/EOS.Web.Tests/DashboardWebHostTests.cs";

    private const string SrcContent = "namespace EOS.Web;\n";

    // ADR-008: the model emits an exact edit block; the diff below is what SeniorEngineer must
    // derive from it deterministically (offsets, counts, markers and paths are never model output).
    private static readonly string ValidEditBlocks =
        $"[EDIT]\nFILE: {SrcPath}\nSEARCH:\nnamespace EOS.Web;\nREPLACE:\nnamespace EOS.Web;\n// added\n[/EDIT]";

    private static readonly string ValidDiff =
        $"--- a/{SrcPath}\n+++ b/{SrcPath}\n@@ -1,1 +1,2 @@\n namespace EOS.Web;\n+// added\n";

    private static DispatchedTask CreateTask(string description) => new(
        TaskId: Guid.NewGuid(), PlanId: Guid.NewGuid(), GoalId: Guid.NewGuid(), Description: description,
        CompetencyRequirements: [], DependsOnTaskIds: [], Priority: 1, State: TaskLifecycleState.Running,
        SchedulingMode: SchedulingMode.Immediate, NotBefore: null, RunningAt: DateTimeOffset.UtcNow,
        EventObserved: false, RetryCount: 0, BlockedReason: null);

    [Fact]
    public void ExtractReferencedPaths_IsDeterministic_DeduplicatedAndOrdered()
    {
        var description = $"Add an endpoint in {SrcPath}, with a test in {TestPath}. Also touch {SrcPath}.";

        var paths = SeniorEngineer.ExtractReferencedPaths(description);

        Assert.Equal([SrcPath, TestPath], paths);
    }

    [Fact]
    public void ExtractReferencedPaths_IgnoresPathsOutsideThePermittedRoots()
    {
        var paths = SeniorEngineer.ExtractReferencedPaths("Edit config/Security.json and docs/x.md and src/../config/Thresholds.json.");

        Assert.Empty(paths);
    }

    [Fact]
    public async Task ExecuteAsync_Throws_WhenTheTaskReferencesNoPath()
    {
        var registry = new RecordingArtifactRegistryClient();
        var role = new SeniorEngineer(new FixedReasoningEngineClient(ValidEditBlocks), new FixedWorkspaceClient { [SrcPath] = SrcContent }, registry);

        await Assert.ThrowsAsync<InvalidOperationException>(() => role.ExecuteAsync(CreateTask("Add a health endpoint somewhere.")));

        Assert.Empty(registry.Registered);
    }

    [Fact]
    public async Task ExecuteAsync_ReadsOnlyReferencedFiles_MarksMissingOnes_AndMakesExactlyOneReasoningCall()
    {
        var workspace = new FixedWorkspaceClient { [SrcPath] = SrcContent };
        var reasoning = new FixedReasoningEngineClient(ValidEditBlocks);
        var role = new SeniorEngineer(reasoning, workspace, new RecordingArtifactRegistryClient());

        await role.ExecuteAsync(CreateTask($"Change {SrcPath} and add {TestPath}."));

        Assert.Equal([SrcPath, TestPath], workspace.ReadPaths);
        Assert.Equal(1, reasoning.CallCount);
        var request = reasoning.LastRequest!;
        Assert.Equal(SeniorEngineer.RoleName, request.RequestingRole);
        Assert.Equal(ReasoningType.EngineeringReasoning, request.ReasoningType);
        Assert.Contains("namespace EOS.Web;", request.Goal);
        Assert.Contains("(does not exist — create it)", request.Goal);
    }

    [Fact]
    public async Task ExecuteAsync_RegistersTheValidatedDiffAsEvidence_AndReturnsItsReference()
    {
        var registry = new RecordingArtifactRegistryClient();
        var role = new SeniorEngineer(new FixedReasoningEngineClient($"Here you go:\n{ValidEditBlocks}\n"), new FixedWorkspaceClient { [SrcPath] = SrcContent }, registry);

        var result = await role.ExecuteAsync(CreateTask($"Change {SrcPath}."));

        var registered = Assert.Single(registry.Registered);
        Assert.Equal(SeniorEngineer.EvidenceArtifactType, registered.Type);
        Assert.Equal(SeniorEngineer.ProducerName, registered.Producer);
        Assert.Equal(ValidDiff, registered.Content);
        Assert.Equal([$"artifact:{registered.ContentHash}"], result.EvidenceRefs);
    }

    [Theory]
    [InlineData("I would add a GET /health endpoint that returns OK.")]                                                       // prose, no block
    [InlineData("```diff\n--- a/src/EOS.Web/DashboardWebHost.cs\n+++ b/src/EOS.Web/DashboardWebHost.cs\n@@ -1 +1 @@\n-x\n+y\n```")] // a diff, not blocks
    [InlineData("[EDIT]\nFILE: src/EOS.Web/DashboardWebHost.cs\nSEARCH:\nnamespace EOS.Fabricated;\nREPLACE:\nx\n[/EDIT]")]   // SEARCH not found
    [InlineData("[EDIT]\nFILE: src/EOS.Runner/Program.cs\nSEARCH:\nnamespace EOS.Web;\nREPLACE:\nx\n[/EDIT]")]              // unreferenced file
    [InlineData("[EDIT]\nFILE: config/Security.json\nSEARCH:\n{}\nREPLACE:\nx\n[/EDIT]")]                                  // out of scope
    [InlineData("[EDIT]\nFILE: src/EOS.Web/DashboardWebHost.cs\nSEARCH:\nnamespace EOS.Web;\nREPLACE:\nnamespace EOS.Web;\n[/EDIT]")] // no change
    [InlineData("[EDIT]\nFILE: src/EOS.Web/DashboardWebHost.cs\nSEARCH:\nnamespace EOS.Web;\nREPLACE:\nx\n")]                 // unterminated
    public async Task ExecuteAsync_Throws_AndRegistersNothing_WhenTheOutputIsNotAValidInScopeEdit(string output)
    {
        var registry = new RecordingArtifactRegistryClient();
        var role = new SeniorEngineer(new FixedReasoningEngineClient(output), new FixedWorkspaceClient { [SrcPath] = SrcContent }, registry);

        await Assert.ThrowsAsync<InvalidOperationException>(() => role.ExecuteAsync(CreateTask($"Change {SrcPath}.")));

        Assert.Empty(registry.Registered);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesReasoningFailure_AndRegistersNothing()
    {
        var registry = new RecordingArtifactRegistryClient();
        var role = new SeniorEngineer(new ThrowingReasoningEngineClient(), new FixedWorkspaceClient { [SrcPath] = SrcContent }, registry);

        var exception = await Assert.ThrowsAsync<ReasoningFailedException>(() => role.ExecuteAsync(CreateTask($"Change {SrcPath}.")));

        Assert.Equal(ReasoningFailureMode.InternalError, exception.FailureMode);
        Assert.Empty(registry.Registered);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesWorkspaceFailure_WithoutReasoning()
    {
        var reasoning = new FixedReasoningEngineClient(ValidEditBlocks);
        var role = new SeniorEngineer(reasoning, new ThrowingWorkspaceClient(), new RecordingArtifactRegistryClient());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => role.ExecuteAsync(CreateTask($"Change {SrcPath}.")));

        Assert.Equal(0, reasoning.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesArtifactRegistrationFailure()
    {
        var role = new SeniorEngineer(new FixedReasoningEngineClient(ValidEditBlocks), new FixedWorkspaceClient { [SrcPath] = SrcContent }, new ThrowingArtifactRegistryClient());

        await Assert.ThrowsAsync<InvalidOperationException>(() => role.ExecuteAsync(CreateTask($"Change {SrcPath}.")));
    }

    // ADR-007: applicability is checked after structural validation and before registration.
    [Fact]
    public async Task ExecuteAsync_ChecksApplicability_ThenRegisters_WhenTheDiffApplies()
    {
        var workspace = new FixedWorkspaceClient { [SrcPath] = SrcContent };
        var registry = new RecordingArtifactRegistryClient();
        var role = new SeniorEngineer(new FixedReasoningEngineClient(ValidEditBlocks), workspace, registry);

        var result = await role.ExecuteAsync(CreateTask($"Change {SrcPath}."));

        Assert.Equal([ValidDiff], workspace.CheckedDiffs);
        var registered = Assert.Single(registry.Registered);
        Assert.Equal(ValidDiff, registered.Content);
        Assert.Equal([$"artifact:{registered.ContentHash}"], result.EvidenceRefs);
    }

    [Fact]
    public async Task ExecuteAsync_Throws_AndRegistersNothing_WhenTheDiffIsStructurallyValidButNotApplicable()
    {
        var workspace = new FixedWorkspaceClient { [SrcPath] = SrcContent, Applicability = new PatchApplicabilityResult(false, "git apply --check exited with code 1: patch does not apply") };
        var registry = new RecordingArtifactRegistryClient();
        var role = new SeniorEngineer(new FixedReasoningEngineClient(ValidEditBlocks), workspace, registry);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => role.ExecuteAsync(CreateTask($"Change {SrcPath}.")));

        Assert.Contains("does not apply to the workspace", exception.Message);
        Assert.Contains("patch does not apply", exception.Message);
        Assert.Single(workspace.CheckedDiffs);
        Assert.Empty(registry.Registered);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotInvokeTheApplicabilityCheck_WhenStructuralValidationFails()
    {
        var workspace = new FixedWorkspaceClient { [SrcPath] = SrcContent };
        var registry = new RecordingArtifactRegistryClient();
        // Output that never yields a diff to validate (no edit block) — the applicability check
        // must not be reached; the structural gate on the derived diff is the next line of defence.
        var role = new SeniorEngineer(
            new FixedReasoningEngineClient("```diff\n--- a/src/EOS.Web/DashboardWebHost.cs\n+++ b/src/EOS.Web/DashboardWebHost.cs\n-x\n+y\n```"),
            workspace, registry);

        await Assert.ThrowsAsync<InvalidOperationException>(() => role.ExecuteAsync(CreateTask($"Change {SrcPath}.")));

        Assert.Empty(workspace.CheckedDiffs);
        Assert.Empty(registry.Registered);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesApplicabilityCheckDenial_AndRegistersNothing()
    {
        var registry = new RecordingArtifactRegistryClient();
        var workspace = new DenyingCheckWorkspaceClient { [SrcPath] = "namespace EOS.Web;\n" };
        var role = new SeniorEngineer(new FixedReasoningEngineClient(ValidEditBlocks), workspace, registry);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => role.ExecuteAsync(CreateTask($"Change {SrcPath}.")));

        Assert.Empty(registry.Registered);
    }

    private sealed class DenyingCheckWorkspaceClient : IWorkspaceClient
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

        public string this[string path]
        {
            set => _files[path] = value;
        }

        public Task<string?> ReadFileAsync(string relativePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(_files.TryGetValue(relativePath, out var content) ? content : null);

        public Task<PatchApplicabilityResult> CheckPatchAppliesAsync(string unifiedDiff, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("Protection denied WorkspaceApplicabilityCheck.");
    }

    // Constitution Part 1 §1.2 / Part 11 §11.2 / R-02: the role project's only reference is
    // EOS.Contracts, so IAIProviderClient (EOS.SDK) is not even resolvable from this project —
    // the compile-time proof lives in EOS.ArchitectureTests; this test pins the runtime shape.
    [Fact]
    public void SeniorEngineer_DependsOnlyOnContractsInterfaces()
    {
        var constructor = Assert.Single(typeof(SeniorEngineer).GetConstructors());

        Assert.All(constructor.GetParameters(), parameter => Assert.Equal("EOS.Contracts", parameter.ParameterType.Namespace));
        Assert.DoesNotContain(
            typeof(SeniorEngineer).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name is "EOS.SDK" or "EOS.AIProvider" or "EOS.Infrastructure" or "EOS.Orchestrator" or "EOS.Reasoning");
    }

    private sealed class FixedWorkspaceClient : IWorkspaceClient
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

        public List<string> ReadPaths { get; } = [];

        public List<string> CheckedDiffs { get; } = [];

        // ADR-007: the applicability verdict this double returns; applicable unless a test says otherwise.
        public PatchApplicabilityResult Applicability { get; set; } = new(true, null);

        public string this[string path]
        {
            set => _files[path] = value;
        }

        public Task<string?> ReadFileAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            ReadPaths.Add(relativePath);
            return Task.FromResult(_files.TryGetValue(relativePath, out var content) ? content : null);
        }

        public Task<PatchApplicabilityResult> CheckPatchAppliesAsync(string unifiedDiff, CancellationToken cancellationToken = default)
        {
            CheckedDiffs.Add(unifiedDiff);
            return Task.FromResult(Applicability);
        }
    }

    private sealed class ThrowingWorkspaceClient : IWorkspaceClient
    {
        public Task<string?> ReadFileAsync(string relativePath, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("Protection denied WorkspaceRead.");

        public Task<PatchApplicabilityResult> CheckPatchAppliesAsync(string unifiedDiff, CancellationToken cancellationToken = default) =>
            throw new UnauthorizedAccessException("Protection denied WorkspaceApplicabilityCheck.");
    }

    private sealed class FixedReasoningEngineClient(string selectedHypothesis) : IReasoningEngineClient
    {
        public int CallCount { get; private set; }

        public ReasoningRequest? LastRequest { get; private set; }

        public Task<Decision[]> ReasonAsync(ReasoningRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult<Decision[]>(
            [
                new Decision(
                    Guid.NewGuid(), request.RequestId, ReasoningType.EngineeringReasoning, selectedHypothesis, [], ["inference:test"],
                    0.5, new Explanation("test", ["inference:test"], [], [], "test", []), "n/a", 0, false, DateTimeOffset.UtcNow),
            ]);
        }

        public Task<ConfidenceGuardResult> CompareAsync(PipelineRecord subject, IEnumerable<PipelineRecord> candidates, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TrustSignal> GetTrustSignalAsync(string sourceRole, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Summary> SummarizeAsync(string content, int? sizeBudget = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingReasoningEngineClient : IReasoningEngineClient
    {
        public Task<Decision[]> ReasonAsync(ReasoningRequest request, CancellationToken cancellationToken = default) =>
            throw new ReasoningFailedException(ReasoningFailureMode.InternalError, "provider unavailable");

        public Task<ConfidenceGuardResult> CompareAsync(PipelineRecord subject, IEnumerable<PipelineRecord> candidates, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TrustSignal> GetTrustSignalAsync(string sourceRole, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Summary> SummarizeAsync(string content, int? sizeBudget = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingArtifactRegistryClient : IArtifactRegistryClient
    {
        public List<ArtifactRecord> Registered { get; } = [];

        public Task<ArtifactRecord> RegisterAsync(string type, string producer, string content, string? previousVersionHash = null, CancellationToken cancellationToken = default)
        {
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content)));
            var record = new ArtifactRecord(hash, type, producer, content, previousVersionHash, DateTimeOffset.UtcNow);
            Registered.Add(record);
            return Task.FromResult(record);
        }

        public Task<ArtifactRecord?> GetByHashAsync(string contentHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(Registered.FirstOrDefault(r => r.ContentHash == contentHash));
    }

    private sealed class ThrowingArtifactRegistryClient : IArtifactRegistryClient
    {
        public Task<ArtifactRecord> RegisterAsync(string type, string producer, string content, string? previousVersionHash = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SQL Server unavailable.");

        public Task<ArtifactRecord?> GetByHashAsync(string contentHash, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SQL Server unavailable.");
    }
}
