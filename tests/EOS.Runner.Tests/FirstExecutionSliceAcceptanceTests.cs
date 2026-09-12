using EOS.Contracts;
using EOS.Gates;
using EOS.Infrastructure;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using EOS.Orchestrator;
using EOS.Planner;
using Microsoft.Extensions.Logging.Abstractions;
using SeniorEngineerRole = EOS.SeniorEngineer.SeniorEngineer;

namespace EOS.Runner.Tests;

/// <summary>
/// Post-Roadmap WP-A acceptance: the first engineering execution slice end to end through the
/// committed runtime components — real SQL Server stores (Goal/Plan/DispatchedTask/LoopIteration/
/// OperationalMode/Artifact/EventStore), the real <see cref="ProtectionGate"/>, the real
/// <see cref="Scheduler"/>/<see cref="ExecutionCoordinator"/>/<see cref="LoopController"/>, the
/// real <see cref="SeniorEngineer"/>, <see cref="WorkspaceReader"/> (on a temp root) and
/// <see cref="ArtifactStore"/>, and the exact <c>Program.cs</c> EventMediator adapters. Only the
/// Reasoning Engine (returns a deterministic diff) and Knowledge retrieval (no reusable planning
/// patterns, so decomposition is the Goal statement itself) are doubled — the real Ollama path is
/// exercised by the runtime demo, never here.
/// </summary>
public class FirstExecutionSliceAcceptanceTests : IDisposable
{
    private const string SrcPath = "src/EOS.Web/DashboardWebHost.cs";
    private const string TestPath = "tests/EOS.Web.Tests/DashboardWebHostTests.cs";
    private const string TaskStatement = $"Add a GET /health endpoint in {SrcPath}, with an automated test in {TestPath}";

    // ADR-008: the (doubled) model emits exact edit blocks — one modification of the existing
    // source file and one creation of the missing test file; SeniorEngineer derives the diff.
    private static readonly string ValidEditBlocks =
        $"[EDIT]\nFILE: {SrcPath}\nSEARCH:\nnamespace EOS.Web;\nREPLACE:\nnamespace EOS.Web;\n// health\n[/EDIT]\n"
        + $"[EDIT]\nFILE: {TestPath}\nSEARCH:\nREPLACE:\n// health tests\n[/EDIT]";

    // The unified diff SeniorEngineer must derive from ValidEditBlocks against the temp workspace.
    private static readonly string ValidDiff =
        $"--- a/{SrcPath}\n+++ b/{SrcPath}\n@@ -1,1 +1,2 @@\n namespace EOS.Web;\n+// health\n--- /dev/null\n+++ b/{TestPath}\n@@ -0,0 +1,1 @@\n+// health tests\n";

    private static string SqlConnectionString =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");

    private readonly string _workspaceRoot;

    public FirstExecutionSliceAcceptanceTests()
    {
        _workspaceRoot = Path.Combine(Path.GetTempPath(), $"eos-wpa-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_workspaceRoot, "src", "EOS.Web"));
        File.WriteAllText(Path.Combine(_workspaceRoot, SrcPath.Replace('/', Path.DirectorySeparatorChar)), "namespace EOS.Web;\n");
    }

    public void Dispose() => Directory.Delete(_workspaceRoot, recursive: true);

    [Fact]
    public async Task ManualRequest_ExecutesTheTaskThroughTheSeniorEngineer_RegistersEvidence_AndPersistsTaskCompleted()
    {
        // Fake temp workspace (no projects): Universal Gates 1–2 are doubled as passing here; the
        // real gates on the real repository are covered by the ADR-009 tests below.
        var stack = await BuildStackAsync(new FixedReasoningEngineClient(ValidEditBlocks), universalGates: new PassingUniversalGateClient());

        await stack.LoopController.RunIterationAsync(new TriggerContext("ManualRequest", TaskStatement), CancellationToken.None);

        var task = Assert.Single(stack.StartedTaskIds.Select(id => stack.DispatchedTaskStore.GetByIdAsync(id, CancellationToken.None).Result)!);
        Assert.Equal(TaskLifecycleState.Review, task!.State);
        Assert.Null(task.BlockedReason);

        var completed = Assert.Single(stack.CompletedTasks);
        Assert.Equal(task.TaskId, completed.TaskId);
        var evidenceRef = Assert.Single(completed.EvidenceRefs);
        Assert.StartsWith("artifact:", evidenceRef);
        var artifact = await stack.ArtifactStore.GetByHashAsync(evidenceRef["artifact:".Length..]);
        Assert.NotNull(artifact);
        Assert.Equal(ValidDiff, artifact.Content);
        Assert.Equal(SeniorEngineerRole.EvidenceArtifactType, artifact.Type);
        Assert.Equal(SeniorEngineerRole.ProducerName, artifact.Producer);
        Assert.Equal(ArtifactStore.ComputeContentHash(ValidDiff), artifact.ContentHash);

        var storedEvents = await stack.EventStore.GetRecentAsync(50, CancellationToken.None);
        var storedCompleted = Assert.Single(storedEvents, e => e.EventType == "TaskCompleted" && e.PayloadJson.Contains(task.TaskId.ToString()));
        Assert.Contains(evidenceRef, storedCompleted.PayloadJson);
        Assert.Contains(storedEvents, e => e.EventType == "GoalCreated");
        Assert.Contains(storedEvents, e => e.EventType == "TaskStarted" && e.PayloadJson.Contains(task.TaskId.ToString()));
        Assert.Empty(stack.BlockedTasks);

        var iteration = (await stack.LoopIterationStore.GetLatestAsync(CancellationToken.None))!;
        Assert.Equal("ManualRequest", iteration.TriggerSource);
        Assert.Equal("Completed", iteration.Outcome);
        Assert.Contains(10, iteration.StepsTraversed);

        // ADR-007: the artifact passed a real `git apply --check` against the temp workspace, and
        // that check is read-only — the workspace is exactly as it was before the run.
        Assert.Equal("namespace EOS.Web;\n", File.ReadAllText(Path.Combine(_workspaceRoot, SrcPath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(_workspaceRoot, TestPath.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task ManualRequest_BlocksTheTask_AndPersistsTaskBlocked_WhenTheDiffIsInvalid()
    {
        var stack = await BuildStackAsync(new FixedReasoningEngineClient("I would add a GET /health endpoint that returns OK."));

        await stack.LoopController.RunIterationAsync(new TriggerContext("ManualRequest", TaskStatement), CancellationToken.None);

        var taskId = Assert.Single(stack.StartedTaskIds);
        var task = (await stack.DispatchedTaskStore.GetByIdAsync(taskId, CancellationToken.None))!;
        Assert.Equal(TaskLifecycleState.Blocked, task.State);
        Assert.StartsWith("Execution failed:", task.BlockedReason);
        Assert.Empty(stack.CompletedTasks);
        var blocked = Assert.Single(stack.BlockedTasks);
        Assert.Equal(taskId, blocked.TaskId);

        var storedEvents = await stack.EventStore.GetRecentAsync(50, CancellationToken.None);
        Assert.Contains(storedEvents, e => e.EventType == "TaskBlocked" && e.PayloadJson.Contains(taskId.ToString()));
        Assert.DoesNotContain(storedEvents, e => e.EventType == "TaskCompleted" && e.PayloadJson.Contains(taskId.ToString()));
        Assert.Null(await stack.ArtifactStore.GetByHashAsync(ArtifactStore.ComputeContentHash("I would add a GET /health endpoint that returns OK.")));

        // One bounded nested Failure iteration (Program.cs wiring), then the outer iteration Failed.
        Assert.Equal(1, stack.FailureIterations);
    }

    // ADR-007 + ADR-008: a structurally valid diff that does not apply to the real workspace must
    // never become evidence. With the model confined to edit blocks, the derived diff can only be
    // non-applicable when the content the role read differs from the workspace at check time (a
    // stale read / concurrent edit) — modelled here by a workspace wrapper that serves stale file
    // content while the real WorkspaceReader runs `git apply --check` on the true temp root. The
    // role throws; the existing coordinator path records Running → Blocked + TaskBlocked; nothing
    // is registered; no TaskCompleted; one bounded nested Failure iteration.
    [Fact]
    public async Task ManualRequest_BlocksTheTask_RegistersNoArtifact_WhenTheDiffIsStructurallyValidButNotApplicable()
    {
        var staleDiff = $"--- a/{SrcPath}\n+++ b/{SrcPath}\n@@ -1,1 +1,2 @@\n namespace EOS.Stale;\n+// health\n";
        var stack = await BuildStackAsync(
            new FixedReasoningEngineClient($"[EDIT]\nFILE: {SrcPath}\nSEARCH:\nnamespace EOS.Stale;\nREPLACE:\nnamespace EOS.Stale;\n// health\n[/EDIT]"),
            wrapWorkspace: real => new StaleReadWorkspaceClient(real, SrcPath, "namespace EOS.Stale;\n"));

        await stack.LoopController.RunIterationAsync(new TriggerContext("ManualRequest", TaskStatement), CancellationToken.None);

        var taskId = Assert.Single(stack.StartedTaskIds);
        var task = (await stack.DispatchedTaskStore.GetByIdAsync(taskId, CancellationToken.None))!;
        Assert.Equal(TaskLifecycleState.Blocked, task.State);
        Assert.Contains("does not apply to the workspace", task.BlockedReason);
        Assert.Contains("patch does not apply", task.BlockedReason);
        Assert.Empty(stack.CompletedTasks);
        Assert.Equal(taskId, Assert.Single(stack.BlockedTasks).TaskId);
        Assert.Null(await stack.ArtifactStore.GetByHashAsync(ArtifactStore.ComputeContentHash(staleDiff)));

        var storedEvents = await stack.EventStore.GetRecentAsync(50, CancellationToken.None);
        Assert.Contains(storedEvents, e => e.EventType == "TaskBlocked" && e.PayloadJson.Contains(taskId.ToString()));
        Assert.DoesNotContain(storedEvents, e => e.EventType == "TaskCompleted" && e.PayloadJson.Contains(taskId.ToString()));
        Assert.Equal(1, stack.FailureIterations);

        // The read-only check left the workspace exactly as it was.
        Assert.Equal("namespace EOS.Web;\n", File.ReadAllText(Path.Combine(_workspaceRoot, SrcPath.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(_workspaceRoot, TestPath.Replace('/', Path.DirectorySeparatorChar))));
    }

    // ADR-007 / Step 7: the applicability check passes through the real Protection mechanism
    // under its own Local Files action type, and a non-Allow verdict never reaches the reader.
    [Fact]
    public async Task ProtectionGatedWorkspaceClient_ValidatesWorkspaceApplicabilityCheck_AndNeverReachesTheReaderOnDenial()
    {
        var recording = new RecordingProtectionClient(ProtectionVerdict.Allow);
        var reader = new WorkspaceReader(_workspaceRoot);
        var gated = new ProtectionGatedWorkspaceClient(reader, recording, "SeniorEngineer");

        var result = await gated.CheckPatchAppliesAsync(ValidDiff, CancellationToken.None);

        Assert.True(result.Applies, result.Error);
        var action = Assert.Single(recording.Validated);
        Assert.Equal("WorkspaceApplicabilityCheck", action.ActionType);
        Assert.Equal("SeniorEngineer", action.Actor);

        var denying = new ProtectionGatedWorkspaceClient(new NeverCalledWorkspaceClient(), new RecordingProtectionClient(ProtectionVerdict.Deny), "SeniorEngineer");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denying.CheckPatchAppliesAsync(ValidDiff, CancellationToken.None));
    }

    private sealed class RecordingProtectionClient(ProtectionVerdict verdict) : IProtectionClient
    {
        public List<ActionRequest> Validated { get; } = [];

        public ValidationResult Validate(ActionRequest action)
        {
            Validated.Add(action);
            return new ValidationResult(verdict, RiskTier.Low, verdict == ProtectionVerdict.Allow ? null : "denied by test");
        }
    }

    private sealed class NeverCalledWorkspaceClient : IWorkspaceClient
    {
        public Task<string?> ReadFileAsync(string relativePath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("must not be reached");

        public Task<PatchApplicabilityResult> CheckPatchAppliesAsync(string unifiedDiff, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("must not be reached");
    }

    // Real Protection denial of the TaskCompletion transition. At TaskCompletion's Low risk tier
    // the real ProtectionGate consults no policy list (ValidateLowTier is allow-and-log), so the
    // only real denial mechanism is EmergencyShutdownState — exactly how
    // SchedulerExecutionCoordinatorAcceptanceTests denies dispatch. Shutdown is activated at the
    // role boundary, after the real SeniorEngineer has read the workspace and registered its
    // evidence and before the coordinator validates TaskCompletion — so the denial observed is
    // the completion gate's own, not an earlier WorkspaceRead denial.
    [Fact]
    public async Task ManualRequest_BlocksTheTask_KeepsTheEvidence_AndNeverPublishesTaskCompleted_WhenTheRealGateDeniesTaskCompletion()
    {
        // Gates doubled as passing so the real gate's first denial lands on TaskCompletion itself
        // (with the real gate client, the earlier UniversalGateRun validation is denied first — see
        // the next test).
        var stack = await BuildStackAsync(
            new FixedReasoningEngineClient(ValidEditBlocks),
            executor => new ShutdownAfterExecutionTaskExecutionClient(executor),
            universalGates: new PassingUniversalGateClient());

        await stack.LoopController.RunIterationAsync(new TriggerContext("ManualRequest", TaskStatement), CancellationToken.None);

        var taskId = Assert.Single(stack.StartedTaskIds);
        var task = (await stack.DispatchedTaskStore.GetByIdAsync(taskId, CancellationToken.None))!;
        Assert.Equal(TaskLifecycleState.Blocked, task.State);
        Assert.StartsWith("TaskCompletion denied: Defer", task.BlockedReason);
        Assert.Empty(stack.CompletedTasks);
        var blocked = Assert.Single(stack.BlockedTasks);
        Assert.Equal(taskId, blocked.TaskId);
        var artifact = await stack.ArtifactStore.GetByHashAsync(ArtifactStore.ComputeContentHash(ValidDiff));
        Assert.NotNull(artifact);
        Assert.Contains($"artifact:{artifact.ContentHash}", task.BlockedReason);

        var storedEvents = await stack.EventStore.GetRecentAsync(50, CancellationToken.None);
        Assert.DoesNotContain(storedEvents, e => e.EventType == "TaskCompleted" && e.PayloadJson.Contains(taskId.ToString()));
        Assert.Contains(storedEvents, e => e.EventType == "TaskBlocked" && e.PayloadJson.Contains(taskId.ToString()));
        var iteration = (await stack.LoopIterationStore.GetLatestAsync(CancellationToken.None))!;
        Assert.Equal("Failure", iteration.TriggerSource); // the one bounded nested iteration
        Assert.Equal(1, stack.FailureIterations);
    }

    // ADR-009: the real gate client validates UniversalGateRun before spawning anything; a denial
    // there is a failed gate evaluation (fail closed), never a pass and never a Review transition.
    [Fact]
    public async Task ManualRequest_BlocksTheTask_WhenTheRealGateDeniesTheUniversalGateRun()
    {
        var stack = await BuildStackAsync(
            new FixedReasoningEngineClient(ValidEditBlocks),
            executor => new ShutdownAfterExecutionTaskExecutionClient(executor));

        await stack.LoopController.RunIterationAsync(new TriggerContext("ManualRequest", TaskStatement), CancellationToken.None);

        var taskId = Assert.Single(stack.StartedTaskIds);
        var task = (await stack.DispatchedTaskStore.GetByIdAsync(taskId, CancellationToken.None))!;
        Assert.Equal(TaskLifecycleState.Blocked, task.State);
        Assert.StartsWith("Universal Gate evaluation failed:", task.BlockedReason);
        Assert.Contains("UniversalGateRun", task.BlockedReason);
        Assert.Empty(stack.CompletedTasks);
        Assert.Single(stack.BlockedTasks);
        Assert.Equal(1, stack.FailureIterations);
    }

    // ---------------------------------------------------------------------------------------
    // ADR-009 on the REAL repository root: SeniorEngineer reads the real files, derives the diff,
    // the real ProtectionGatedUniversalGateClient copies the workspace to an isolated directory,
    // applies the diff there, runs `dotnet build` (Gate 1) and `dotnet test` (Gate 2), and the
    // real RuleEngine decides. The real working tree, index and status are never touched.
    // ---------------------------------------------------------------------------------------

    private const string RealSrcPath = "src/EOS.Gates/RuleEngine.cs";
    private const string RealTestPath = "tests/EOS.Gates.Tests/AdrNineAcceptanceProbeTests.cs";
    private const string RealTaskStatement = $"Add a clarifying comment to {RealSrcPath}, with an automated test in {RealTestPath}";

    private static string RealEditBlocks(string testBody) =>
        $"[EDIT]\nFILE: {RealSrcPath}\nSEARCH:\nnamespace EOS.Gates;\nREPLACE:\nnamespace EOS.Gates;\n// ADR-009 acceptance probe\n[/EDIT]\n"
        + $"[EDIT]\nFILE: {RealTestPath}\nSEARCH:\nREPLACE:\nnamespace EOS.Gates.Tests;\n\npublic class AdrNineAcceptanceProbeTests\n{{\n    [Fact]\n    public void Probe()\n    {{\n        {testBody}\n    }}\n}}\n[/EDIT]";

    [Fact]
    public async Task ManualRequest_OnTheRealRepository_PassesRealGates1And2_AndReachesReview_WhenTheChangeCompilesAndItsTestsPass()
    {
        var root = FindRepositoryRoot();
        var before = await SnapshotRealTreeAsync(root);
        var stack = await BuildStackAsync(new FixedReasoningEngineClient(RealEditBlocks("Assert.True(true);")), workspaceRoot: root);

        await stack.LoopController.RunIterationAsync(new TriggerContext("ManualRequest", RealTaskStatement), CancellationToken.None);

        var taskId = Assert.Single(stack.StartedTaskIds);
        var task = (await stack.DispatchedTaskStore.GetByIdAsync(taskId, CancellationToken.None))!;
        Assert.Equal(TaskLifecycleState.Review, task.State);
        Assert.Null(task.BlockedReason);
        var completed = Assert.Single(stack.CompletedTasks);
        var evidenceRef = Assert.Single(completed.EvidenceRefs);
        var artifact = await stack.ArtifactStore.GetByHashAsync(evidenceRef["artifact:".Length..]);
        Assert.NotNull(artifact);
        Assert.Contains("+// ADR-009 acceptance probe", artifact.Content);
        Assert.Contains($"+++ b/{RealTestPath}", artifact.Content);
        Assert.Empty(stack.BlockedTasks);
        var storedEvents = await stack.EventStore.GetRecentAsync(50, CancellationToken.None);
        Assert.Contains(storedEvents, e => e.EventType == "TaskCompleted" && e.PayloadJson.Contains(taskId.ToString()));

        Assert.Equal(before, await SnapshotRealTreeAsync(root));
    }

    [Fact]
    public async Task ManualRequest_OnTheRealRepository_BlocksTheTask_WhenRealGate2Fails()
    {
        var root = FindRepositoryRoot();
        var before = await SnapshotRealTreeAsync(root);
        var stack = await BuildStackAsync(new FixedReasoningEngineClient(RealEditBlocks("Assert.Fail(\"ADR-009 acceptance probe failure\");")), workspaceRoot: root);

        await stack.LoopController.RunIterationAsync(new TriggerContext("ManualRequest", RealTaskStatement), CancellationToken.None);

        var taskId = Assert.Single(stack.StartedTaskIds);
        var task = (await stack.DispatchedTaskStore.GetByIdAsync(taskId, CancellationToken.None))!;
        Assert.Equal(TaskLifecycleState.Blocked, task.State);
        Assert.StartsWith("Universal Gate failure: Universal Gate 2 (unit tests) failed", task.BlockedReason);
        Assert.Contains("ADR-009 acceptance probe failure", task.BlockedReason);
        Assert.Contains("artifact:", task.BlockedReason);
        Assert.Empty(stack.CompletedTasks);
        Assert.Single(stack.BlockedTasks);
        var storedEvents = await stack.EventStore.GetRecentAsync(50, CancellationToken.None);
        Assert.DoesNotContain(storedEvents, e => e.EventType == "TaskCompleted" && e.PayloadJson.Contains(taskId.ToString()));
        Assert.Contains(storedEvents, e => e.EventType == "TaskBlocked" && e.PayloadJson.Contains(taskId.ToString()));
        Assert.Equal(1, stack.FailureIterations);

        Assert.Equal(before, await SnapshotRealTreeAsync(root));
    }

    // Qodo H-2 regression: the changed project (EOS.Contracts) still compiles on its own; a
    // consumer (EOS.Gates) no longer does. Real Gate 1 now builds the dependency closure, so the
    // artifact is blocked and never reaches Review.
    [Fact]
    public async Task ManualRequest_OnTheRealRepository_BlocksTheTask_WhenTheChangeCompilesUpstream_ButBreaksADependentProject()
    {
        const string contractsPath = "src/EOS.Contracts/UniversalGateResult.cs";
        var root = FindRepositoryRoot();
        var before = await SnapshotRealTreeAsync(root);
        var editBlocks = $"[EDIT]\nFILE: {contractsPath}\nSEARCH:\n    NotApplicable,\nREPLACE:\n    NotApplicableRenamed,\n[/EDIT]";
        var stack = await BuildStackAsync(new FixedReasoningEngineClient(editBlocks), workspaceRoot: root);

        await stack.LoopController.RunIterationAsync(
            new TriggerContext("ManualRequest", $"Rename the NotApplicable gate status in {contractsPath}"), CancellationToken.None);

        var taskId = Assert.Single(stack.StartedTaskIds);
        var task = (await stack.DispatchedTaskStore.GetByIdAsync(taskId, CancellationToken.None))!;
        Assert.Equal(TaskLifecycleState.Blocked, task.State);
        Assert.StartsWith("Universal Gate failure: Universal Gate 1 (build/static analysis) failed", task.BlockedReason);
        Assert.Contains("error CS0117", task.BlockedReason);
        Assert.Empty(stack.CompletedTasks);
        Assert.Single(stack.BlockedTasks);
        var storedEvents = await stack.EventStore.GetRecentAsync(50, CancellationToken.None);
        Assert.DoesNotContain(storedEvents, e => e.EventType == "TaskCompleted" && e.PayloadJson.Contains(taskId.ToString()));
        Assert.Contains(storedEvents, e => e.EventType == "TaskBlocked" && e.PayloadJson.Contains(taskId.ToString()));

        Assert.Equal(before, await SnapshotRealTreeAsync(root));
    }

    // Regression for the Attempt-3 real-model artifact (sha256 db434678…): `git apply --check`
    // PASSES against the real repository, yet the test hunk drops a method signature, so the
    // patch does not compile (CS1519). Before ADR-009 this reached Review with TaskCompleted;
    // now Universal Gate 1 blocks it. The executor double registers the exact artifact content.
    [Fact]
    public async Task ManualRequest_OnTheRealRepository_BlocksTheAttempt3Artifact_AtRealGate1_WithCS1519()
    {
        var root = FindRepositoryRoot();
        var before = await SnapshotRealTreeAsync(root);
        var executor = new RegisteredArtifactTaskExecutionClient(Attempt3Artifact);
        var stack = await BuildStackAsync(new FixedReasoningEngineClient("unused"), _ => executor, workspaceRoot: root);

        await stack.LoopController.RunIterationAsync(new TriggerContext("ManualRequest", TaskStatement), CancellationToken.None);

        Assert.True(executor.ApplicabilityResult!.Applies, executor.ApplicabilityResult.Error);
        var taskId = Assert.Single(stack.StartedTaskIds);
        var task = (await stack.DispatchedTaskStore.GetByIdAsync(taskId, CancellationToken.None))!;
        Assert.Equal(TaskLifecycleState.Blocked, task.State);
        Assert.StartsWith("Universal Gate failure: Universal Gate 1 (build/static analysis) failed", task.BlockedReason);
        Assert.Contains("CS1519", task.BlockedReason);
        Assert.Contains($"artifact:{executor.ContentHash}", task.BlockedReason);
        Assert.Empty(stack.CompletedTasks);
        Assert.Single(stack.BlockedTasks);
        var storedEvents = await stack.EventStore.GetRecentAsync(50, CancellationToken.None);
        Assert.DoesNotContain(storedEvents, e => e.EventType == "TaskCompleted" && e.PayloadJson.Contains(taskId.ToString()));
        Assert.Contains(storedEvents, e => e.EventType == "TaskBlocked" && e.PayloadJson.Contains(taskId.ToString()));
        Assert.NotNull(await stack.ArtifactStore.GetByHashAsync(executor.ContentHash));

        Assert.Equal(before, await SnapshotRealTreeAsync(root));
    }

    /// <summary>
    /// Executor double standing in for a role whose artifact is already known: checks applicability
    /// through the real Protection-gated workspace (so the "applies but does not compile" premise is
    /// proven, not assumed), registers the content in the real Artifact Registry, returns its ref.
    /// </summary>
    private sealed class RegisteredArtifactTaskExecutionClient(string unifiedDiff) : ITaskExecutionClient
    {
        public ArtifactStore? ArtifactStore { get; set; }

        public IWorkspaceClient? Workspace { get; set; }

        public PatchApplicabilityResult? ApplicabilityResult { get; private set; }

        public string ContentHash => ArtifactStore.ComputeContentHash(unifiedDiff);

        public async Task<TaskExecutionResult> ExecuteAsync(DispatchedTask task, CancellationToken cancellationToken = default)
        {
            ApplicabilityResult = await Workspace!.CheckPatchAppliesAsync(unifiedDiff, cancellationToken);
            var record = await ArtifactStore!.RegisterAsync(
                SeniorEngineerRole.EvidenceArtifactType, SeniorEngineerRole.ProducerName, unifiedDiff, null, cancellationToken);
            return new TaskExecutionResult([$"artifact:{record.ContentHash}"]);
        }
    }

    private sealed class PassingUniversalGateClient : IUniversalGateClient
    {
        public Task<UniversalGateDecision> EvaluateAsync(DispatchedTask task, IReadOnlyList<string> evidenceRefs, CancellationToken cancellationToken = default) =>
            Task.FromResult(new UniversalGateDecision(true, null, new UniversalGateResult(
                new GateStepResult(GateStepStatus.Passed, "doubled"), new GateStepResult(GateStepStatus.Passed, "doubled"))));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EOS.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root (EOS.slnx not found).");
    }

    /// <summary>The real tree's observable state: the two touched paths, the Git index bytes, and `git status --porcelain`.</summary>
    private static async Task<string> SnapshotRealTreeAsync(string root)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false };
        info.ArgumentList.Add("status");
        info.ArgumentList.Add("--porcelain");
        using var process = System.Diagnostics.Process.Start(info)!;
        var status = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        var index = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(root, ".git", "index"))));
        var source = await File.ReadAllTextAsync(Path.Combine(root, RealSrcPath));
        var web = await File.ReadAllTextAsync(Path.Combine(root, SrcPath));
        return $"{status}\n{index}\n{source}\n{web}\n{File.Exists(Path.Combine(root, RealTestPath))}";
    }

    private const string Attempt3Artifact =
        "--- a/src/EOS.Web/DashboardWebHost.cs\n" +
        "+++ b/src/EOS.Web/DashboardWebHost.cs\n" +
        "@@ -31,6 +31,8 @@\n" +
        "     {\n" +
        "         app.MapGet(\"/\", () => Results.Content(BuildHtml(dashboardOptions.Title), \"text/html\"));\n" +
        " \n" +
        "+        app.MapGet(\"/health\", () => Results.Ok(\"OK\"));\n" +
        "+\n" +
        "         app.MapGet(\"/api/loop-status\", async (CancellationToken cancellationToken) =>\n" +
        "             await dashboardQueryService.GetLoopStatusAsync(cancellationToken));\n" +
        " \n" +
        "--- a/tests/EOS.Web.Tests/DashboardWebHostTests.cs\n" +
        "+++ b/tests/EOS.Web.Tests/DashboardWebHostTests.cs\n" +
        "@@ -72,6 +72,23 @@\n" +
        "     }\n" +
        " \n" +
        "     [Fact]\n" +
        "+    public async Task Health_ReturnsOkAndBodyOK()\n" +
        "+    {\n" +
        "+        var response = await _client!.GetAsync(\"/health\");\n" +
        "+\n" +
        "+        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);\n" +
        "+        Assert.Equal(\"OK\", await response.Content.ReadAsStringAsync());\n" +
        "+    }\n" +
        "+    {\n" +
        "+        var response = await _client!.GetAsync(\"/\");\n" +
        "+        var html = await response.Content.ReadAsStringAsync();\n" +
        "+\n" +
        "+        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);\n" +
        "+        Assert.Contains(\"text/html\", response.Content.Headers.ContentType?.MediaType);\n" +
        "+        Assert.Contains(\"EOS Dashboard Test\", html);\n" +
        "+    }\n" +
        "+\n" +
        "+    [Fact]\n" +
        "     public async Task LoopStatus_ReturnsOkAndTheServiceResult()\n" +
        "     {\n" +
        "         var response = await _client!.GetAsync(\"/api/loop-status\");\n";

    // Test-only boundary wrapper: lets the REAL role execute completely, then activates the real
    // gate's Emergency Shutdown so the next validation — the coordinator's TaskCompletion — is
    // denied by the real ProtectionGate.
    private sealed class ShutdownAfterExecutionTaskExecutionClient(ITaskExecutionClient inner) : ITaskExecutionClient
    {
        public ProtectionGate? ProtectionGate { get; set; }

        public async Task<TaskExecutionResult> ExecuteAsync(DispatchedTask task, CancellationToken cancellationToken = default)
        {
            var result = await inner.ExecuteAsync(task, cancellationToken);
            ProtectionGate!.Validate(new ActionRequest(Guid.NewGuid(), "EmergencyShutdown", "Test", 10));
            return result;
        }
    }

    private sealed class Stack
    {
        public LoopController LoopController { get; set; } = null!;

        public PlanningEngine PlanningEngine { get; set; } = null!;

        public Scheduler Scheduler { get; set; } = null!;

        public ExecutionCoordinator ExecutionCoordinator { get; set; } = null!;

        public required ProtectionGate ProtectionGate { get; init; }

        public required DispatchedTaskStore DispatchedTaskStore { get; init; }

        public required LoopIterationStore LoopIterationStore { get; init; }

        public required ArtifactStore ArtifactStore { get; init; }

        public required SqlEventStore EventStore { get; init; }

        public List<Guid> StartedTaskIds { get; } = [];

        public List<TaskCompletedPayload> CompletedTasks { get; } = [];

        public List<TaskBlockedPayload> BlockedTasks { get; } = [];

        public int FailureIterations { get; set; }
    }

    /// <param name="universalGates">
    /// ADR-009: the Universal Gate client handed to the coordinator. <see langword="null"/> wires the
    /// exact Program.cs adapter — Protection-gated <see cref="IsolatedUniversalGateRunner"/> on
    /// <paramref name="workspaceRoot"/> plus the real <see cref="RuleEngine"/> decision. The fake temp
    /// workspace has no projects, so tests that run on it and need to reach Review pass
    /// <see cref="PassingUniversalGateClient"/> explicitly; the real gates are proven on the real
    /// repository root by the ADR-009 tests below.
    /// </param>
    private async Task<Stack> BuildStackAsync(
        IReasoningEngineClient reasoningEngineClient,
        Func<ITaskExecutionClient, ITaskExecutionClient>? wrapExecutor = null,
        Func<IWorkspaceClient, IWorkspaceClient>? wrapWorkspace = null,
        IUniversalGateClient? universalGates = null,
        string? workspaceRoot = null)
    {
        workspaceRoot ??= _workspaceRoot;
        var goalStore = new GoalStore(SqlConnectionString);
        await goalStore.EnsureTableExistsAsync(CancellationToken.None);
        var planStore = new PlanStore(SqlConnectionString);
        await planStore.EnsureTableExistsAsync(CancellationToken.None);
        var goalDependencyStore = new GoalDependencyStore(SqlConnectionString);
        await goalDependencyStore.EnsureTableExistsAsync(CancellationToken.None);
        var dispatchedTaskStore = new DispatchedTaskStore(SqlConnectionString);
        await dispatchedTaskStore.EnsureTableExistsAsync(CancellationToken.None);

        var loopIterationStore = new LoopIterationStore(SqlConnectionString);
        await loopIterationStore.EnsureTableExistsAsync(CancellationToken.None);
        var operationalModeStore = new OperationalModeStore(SqlConnectionString);
        await operationalModeStore.EnsureTableExistsAsync(CancellationToken.None);
        var artifactStore = new ArtifactStore(SqlConnectionString);
        await artifactStore.EnsureTableExistsAsync(CancellationToken.None);
        var sqlEventStore = new SqlEventStore(SqlConnectionString);
        await sqlEventStore.EnsureTableExistsAsync(CancellationToken.None);

        var ruleEngine = new RuleEngine();
        var protectionGate = new ProtectionGate(
            new PolicyEngine([], [], [], []), ruleEngine, new RiskEngine(), new ApprovalEngine(),
            new EmergencyShutdownState(), new ResourceCeilings(90, 8192, 476000, 100000, 32000, 4),
            new StubResourceManagementClient(), NullLogger<ProtectionGate>.Instance);

        var eventMediator = new EventMediator();
        var stack = new Stack
        {
            ProtectionGate = protectionGate,
            DispatchedTaskStore = dispatchedTaskStore,
            LoopIterationStore = loopIterationStore,
            ArtifactStore = artifactStore,
            EventStore = sqlEventStore,
        };

        void PersistEvent<TPayload>(EventEnvelope<TPayload> envelope) =>
            sqlEventStore.AppendAsync(StoredEventMapper.ToStoredEvent(envelope), CancellationToken.None).GetAwaiter().GetResult();

        eventMediator.Subscribe<GoalCreatedPayload>(PersistEvent);
        eventMediator.Subscribe<TaskCreatedPayload>(PersistEvent);
        eventMediator.Subscribe<TaskStartedPayload>(PersistEvent);
        eventMediator.Subscribe<TaskCompletedPayload>(PersistEvent);
        eventMediator.Subscribe<TaskBlockedPayload>(PersistEvent);
        eventMediator.Subscribe<LoopIterationStartedPayload>(PersistEvent);
        eventMediator.Subscribe<LoopIterationCompletedPayload>(PersistEvent);
        eventMediator.Subscribe<TaskStartedPayload>(e => stack.StartedTaskIds.Add(e.Payload.TaskId));
        eventMediator.Subscribe<TaskCompletedPayload>(e => stack.CompletedTasks.Add(e.Payload));
        eventMediator.Subscribe<TaskBlockedPayload>(e => stack.BlockedTasks.Add(e.Payload));

        var goalManager = new GoalManager(
            goalStore, new EventMediatorGoalCreatedEventPublisher(eventMediator), new EventMediatorGoalCancelledEventPublisher(eventMediator));
        var goalValidator = new GoalValidator(protectionGate, new EventMediatorGoalValidatedEventPublisher(eventMediator));
        var taskGraphBuilder = new TaskGraphBuilder(new NoPatternsKnowledgeClient(), reasoningEngineClient);
        // The DispatchedTask table is shared with every other real-infra suite (no cleanup
        // convention exists in this codebase). The Scheduler's own WP-025 current-Plan filter
        // (IGoalPlanQueryClient) is the production mechanism that decides eligibility, so scoping
        // that adapter to the Goals this test itself creates makes foreign Ready rows ineligible
        // here — without cancelling them (which would interfere with the suites that own them).
        var goalPlanQueryClient = new TestScopedGoalPlanQueryClient(goalStore);
        eventMediator.Subscribe<GoalCreatedPayload>(e => goalPlanQueryClient.Include(e.Payload.GoalId));
        var scheduler = new Scheduler(
            dispatchedTaskStore, new PlanStorePlanQueryClient(planStore), goalPlanQueryClient,
            new StubResourceManagementClient(), concurrencyCeiling: 1_000_000, dailyCapacity: 1_000_000);
        var planningEngine = new PlanningEngine(
            goalManager, goalValidator, taskGraphBuilder, new DependencyManager(goalDependencyStore, goalStore), new PriorityManager(), planStore,
            new EventMediatorTaskCreatedEventPublisher(eventMediator),
            new EventMediatorPlannerGeneratedEventPublisher(eventMediator),
            new EventMediatorReplanTriggeredEventPublisher(eventMediator));
        eventMediator.Subscribe<TaskCreatedPayload>(e => scheduler.OnTaskCreated(e.Payload.TaskId, e.Payload.Priority));
        eventMediator.Subscribe<PlannerGeneratedPayload>(e => scheduler.OnPlannerGeneratedAsync(e.Payload.PlanId, CancellationToken.None).GetAwaiter().GetResult());

        IWorkspaceClient workspace = new ProtectionGatedWorkspaceClient(new WorkspaceReader(workspaceRoot), protectionGate, SeniorEngineerRole.RoleName);
        var seniorEngineer = new SeniorEngineerRole(
            reasoningEngineClient,
            wrapWorkspace is null ? workspace : wrapWorkspace(workspace),
            artifactStore);
        ITaskExecutionClient executor = wrapExecutor is null ? seniorEngineer : wrapExecutor(seniorEngineer);
        if (executor is ShutdownAfterExecutionTaskExecutionClient shutdownWrapper)
        {
            shutdownWrapper.ProtectionGate = protectionGate;
        }

        if (executor is RegisteredArtifactTaskExecutionClient artifactExecutor)
        {
            artifactExecutor.ArtifactStore = artifactStore;
            artifactExecutor.Workspace = workspace;
        }

        universalGates ??= new ProtectionGatedUniversalGateClient(
            artifactStore, new IsolatedUniversalGateRunner(workspaceRoot), ruleEngine, protectionGate, SeniorEngineerRole.RoleName);

        var executionCoordinator = new ExecutionCoordinator(
            scheduler, dispatchedTaskStore, protectionGate,
            new EventMediatorTaskStartedEventPublisher(eventMediator), executor,
            new EventMediatorTaskCompletedEventPublisher(eventMediator), new EventMediatorTaskBlockedEventPublisher(eventMediator),
            universalGates);

        var loopController = new LoopController(
            planningEngine, reasoningEngineClient, protectionGate, new StubResourceManagementClient(), scheduler, executionCoordinator,
            new ProgressMonitor(dispatchedTaskStore, goalPlanQueryClient), loopIterationStore,
            new EventMediatorLoopIterationStartedEventPublisher(eventMediator), new EventMediatorLoopIterationCompletedEventPublisher(eventMediator),
            operationalModeStore, new EventMediatorOperationalModeChangedEventPublisher(eventMediator),
            new EventMediatorLoopIterationEvaluatedEventPublisher(eventMediator));

        // The exact Program.cs Failure-trigger wiring: TaskBlocked → one nested "Failure" iteration.
        eventMediator.Subscribe<TaskBlockedPayload>(e =>
        {
            stack.FailureIterations++;
            loopController.RunIterationAsync(new TriggerContext("Failure", e.Payload.TaskId.ToString()), CancellationToken.None).GetAwaiter().GetResult();
        });

        stack.LoopController = loopController;
        stack.PlanningEngine = planningEngine;
        stack.Scheduler = scheduler;
        stack.ExecutionCoordinator = executionCoordinator;
        return stack;
    }

    private sealed class TestScopedGoalPlanQueryClient(GoalStore goalStore) : IGoalPlanQueryClient
    {
        private readonly HashSet<Guid> _ownGoalIds = [];

        public void Include(Guid goalId) => _ownGoalIds.Add(goalId);

        public async Task<Guid?> GetCurrentPlanIdAsync(Guid goalId, CancellationToken cancellationToken = default) =>
            _ownGoalIds.Contains(goalId) ? (await goalStore.GetByIdAsync(goalId, cancellationToken))?.PlanId : null;
    }

    // Serves stale content for one path on read; every applicability check still goes to the real,
    // Protection-gated reader against the true workspace.
    private sealed class StaleReadWorkspaceClient(IWorkspaceClient real, string stalePath, string staleContent) : IWorkspaceClient
    {
        public Task<string?> ReadFileAsync(string relativePath, CancellationToken cancellationToken = default) =>
            relativePath == stalePath ? Task.FromResult<string?>(staleContent) : real.ReadFileAsync(relativePath, cancellationToken);

        public Task<PatchApplicabilityResult> CheckPatchAppliesAsync(string unifiedDiff, CancellationToken cancellationToken = default) =>
            real.CheckPatchAppliesAsync(unifiedDiff, cancellationToken);
    }

    private sealed class NoPatternsKnowledgeClient : IKnowledgeClient
    {
        public Task UpdateAsync(Guid nodeId, KnowledgeNodeType nodeType, string content, string[] domainTags, string[] evidenceRefs, KnowledgeMetadata? metadata = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IEnumerable<KnowledgeNode>> QueryAsync(MemoryType? type, string[]? domainTags, DateRange? range, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<KnowledgeNode>>([]);

        public Task<IEnumerable<KnowledgeNode>> QuerySimilarAsync(Guid nodeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ContextPayload> AssembleContextAsync(ContextRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Guid> ConsolidateAsync(MemoryRef source, string reason, string[] evidenceRefs, bool suppressLessonLearned = false, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedReasoningEngineClient(string selectedHypothesis) : IReasoningEngineClient
    {
        public Task<Decision[]> ReasonAsync(ReasoningRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<Decision[]>(
            [
                new Decision(
                    Guid.NewGuid(), request.RequestId, request.ReasoningType ?? ReasoningType.EngineeringReasoning, selectedHypothesis, [],
                    ["inference:test"], 0.5, new Explanation("test", ["inference:test"], [], [], "test", []), "n/a", 0, false, DateTimeOffset.UtcNow),
            ]);

        public Task<ConfidenceGuardResult> CompareAsync(PipelineRecord subject, IEnumerable<PipelineRecord> candidates, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<TrustSignal> GetTrustSignalAsync(string sourceRole, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Summary> SummarizeAsync(string content, int? sizeBudget = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubResourceManagementClient : IResourceManagementClient
    {
        public double GetCurrentBudget(ResourceType resourceType) => 0;

        public CapacityTier GetCurrentTier(ResourceType resourceType) => CapacityTier.Safe;

        public ModelResidencyStatus GetModelResidency(string modelId) => new(modelId, ModelResidencyState.Unloaded, null);

        public void RequestBackgroundSlot(string jobId, ResourceClass resourceClass)
        {
        }
    }
}
