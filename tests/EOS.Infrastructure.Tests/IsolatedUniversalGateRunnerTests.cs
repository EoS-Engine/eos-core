using System.Runtime.Versioning;
using System.Security.Cryptography;
using EOS.Contracts;
using EOS.Infrastructure;

namespace EOS.Infrastructure.Tests;

// ADR-009: Universal Gates 1–2 measured in an isolated throwaway copy — never the real working
// tree. The real-repository tests below run the actual toolchain (git apply, dotnet build,
// dotnet test) against a copy of this repository and prove the real tree, index and status are
// untouched. The stub tests use POSIX shell scripts with Unix file modes (Linux-only, scoped to
// this class like WorkspaceReaderTests).
[SupportedOSPlatform("linux")]
public class IsolatedUniversalGateRunnerTests : IDisposable
{
    private const string GatesSource = "src/EOS.Gates/RuleEngine.cs";
    private const string ContractsSource = "src/EOS.Contracts/UniversalGateResult.cs";
    private const string GatesTestProject = "tests/EOS.Gates.Tests/EOS.Gates.Tests.csproj";

    private readonly string _scratch;

    public IsolatedUniversalGateRunnerTests()
    {
        _scratch = Path.Combine(Path.GetTempPath(), $"eos-gate-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratch);
    }

    public void Dispose()
    {
        Directory.Delete(_scratch, recursive: true);
    }

    // ------------------------------------------------------------------------------------
    // Deterministic project ownership and path extraction.
    // ------------------------------------------------------------------------------------

    [Fact]
    public void ExtractChangedPaths_ReturnsEveryOldAndNewPath_Deduplicated_InOrder()
    {
        var diff = "--- a/src/A/x.cs\n+++ b/src/A/x.cs\n@@ -1 +1 @@\n-a\n+b\n--- /dev/null\n+++ b/tests/A.Tests/y.cs\n@@ -0,0 +1 @@\n+c\n--- a/src/B/z.cs\n+++ /dev/null\n@@ -1 +0,0 @@\n-d\n";

        var paths = IsolatedUniversalGateRunner.ExtractChangedPaths(diff);

        Assert.Equal(["src/A/x.cs", "tests/A.Tests/y.cs", "src/B/z.cs"], paths);
    }

    [Fact]
    public void ResolveOwningProject_MapsSrcAndTestsPaths_ToTheProjectFileInThatDirectory()
    {
        var root = FindRepositoryRoot();

        Assert.Equal("src/EOS.Gates/EOS.Gates.csproj", IsolatedUniversalGateRunner.ResolveOwningProject(root, GatesSource));
        Assert.Equal(GatesTestProject, IsolatedUniversalGateRunner.ResolveOwningProject(root, "tests/EOS.Gates.Tests/RuleEngineTests.cs"));
        Assert.Null(IsolatedUniversalGateRunner.ResolveOwningProject(root, "docs/EOS-Specification.md"));
        Assert.Null(IsolatedUniversalGateRunner.ResolveOwningProject(root, "src/EOS.DoesNotExist/x.cs"));
        Assert.Null(IsolatedUniversalGateRunner.ResolveOwningProject(root, "src/../src/EOS.Gates/x.cs"));
    }

    // ------------------------------------------------------------------------------------
    // Real repository, real toolchain.
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_PassesGate1_AndReportsGate2NotApplicable_ForACompilingChangeWithoutATestProject()
    {
        var root = FindRepositoryRoot();
        var runner = new IsolatedUniversalGateRunner(root);

        var result = await runner.RunAsync(InsertAtTop(root, ContractsSource, "// ADR-009 gate probe: compiles, no test project owns this path."));

        Assert.Equal(GateStepStatus.Passed, result.BuildGate.Status);
        Assert.Contains("src/EOS.Contracts/EOS.Contracts.csproj", result.BuildGate.Detail);
        Assert.Equal(GateStepStatus.NotApplicable, result.TestGate.Status);
    }

    [Fact]
    public async Task RunAsync_PassesBothGates_ForACompilingChangeWhoseTestsPass()
    {
        var root = FindRepositoryRoot();
        var runner = new IsolatedUniversalGateRunner(root);
        var diff = InsertAtTop(root, GatesSource, "// ADR-009 gate probe: compiles; EOS.Gates.Tests exercises this project.")
            + CreateFile("tests/EOS.Gates.Tests/GateProbeTests.cs", ProbeTest("Assert.True(true);"));

        var result = await runner.RunAsync(diff);

        Assert.Equal(GateStepStatus.Passed, result.BuildGate.Status);
        Assert.Contains("src/EOS.Gates/EOS.Gates.csproj", result.BuildGate.Detail);
        Assert.Contains(GatesTestProject, result.BuildGate.Detail);
        Assert.Equal(GateStepStatus.Passed, result.TestGate.Status);
        Assert.Contains(GatesTestProject, result.TestGate.Detail);
    }

    [Fact]
    public async Task RunAsync_FailsGate1_WithTheCompilerError_AndDoesNotRunGate2_WhenTheChangeDoesNotCompile()
    {
        var root = FindRepositoryRoot();
        var runner = new IsolatedUniversalGateRunner(root);

        var result = await runner.RunAsync(InsertAtTop(root, GatesSource, "this is not C#"));

        Assert.Equal(GateStepStatus.Failed, result.BuildGate.Status);
        Assert.Contains("error CS", result.BuildGate.Detail);
        Assert.Equal(GateStepStatus.NotApplicable, result.TestGate.Status);
        Assert.Contains("Not run", result.TestGate.Detail);
    }

    [Fact]
    public async Task RunAsync_FailsGate2_WithTheTestOutput_WhenATestFails()
    {
        var root = FindRepositoryRoot();
        var runner = new IsolatedUniversalGateRunner(root);

        var result = await runner.RunAsync(CreateFile("tests/EOS.Gates.Tests/GateProbeTests.cs", ProbeTest("Assert.Fail(\"ADR-009 gate probe failure\");")));

        Assert.Equal(GateStepStatus.Passed, result.BuildGate.Status);
        Assert.Equal(GateStepStatus.Failed, result.TestGate.Status);
        Assert.Contains("ADR-009 gate probe failure", result.TestGate.Detail);
    }

    // Attempt-3 artifact regression (real model output db434678…): applicability PASS, yet the
    // test hunk drops a method signature — the very case Universal Gate 1 exists to catch.
    [Fact]
    public async Task RunAsync_FailsGate1_WithCS1519_ForTheAttempt3Artifact()
    {
        var root = FindRepositoryRoot();
        var runner = new IsolatedUniversalGateRunner(root);

        var result = await runner.RunAsync(Attempt3Artifact);

        Assert.Equal(GateStepStatus.Failed, result.BuildGate.Status);
        Assert.Contains("CS1519", result.BuildGate.Detail);
        Assert.Equal(GateStepStatus.NotApplicable, result.TestGate.Status);
    }

    [Fact]
    public async Task RunAsync_FailsGate1_WhenTheDiffDoesNotApply()
    {
        var root = FindRepositoryRoot();
        var runner = new IsolatedUniversalGateRunner(root);
        var diff = $"--- a/{GatesSource}\n+++ b/{GatesSource}\n@@ -1,1 +1,1 @@\n-this line does not exist in the file\n+replacement\n";

        var result = await runner.RunAsync(diff);

        Assert.Equal(GateStepStatus.Failed, result.BuildGate.Status);
        Assert.Contains("git apply failed", result.BuildGate.Detail);
        Assert.Equal(GateStepStatus.NotApplicable, result.TestGate.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a diff at all\n")]
    public async Task RunAsync_FailsGate1_WhenTheDiffNamesNoFile(string diff)
    {
        var runner = new IsolatedUniversalGateRunner(FindRepositoryRoot());

        var result = await runner.RunAsync(diff);

        Assert.Equal(GateStepStatus.Failed, result.BuildGate.Status);
        Assert.Equal(GateStepStatus.NotApplicable, result.TestGate.Status);
    }

    [Fact]
    public async Task RunAsync_FailsGate1_WhenAChangedPathHasNoOwningProject()
    {
        var runner = new IsolatedUniversalGateRunner(FindRepositoryRoot());
        var diff = "--- a/docs/EOS-Specification.md\n+++ b/docs/EOS-Specification.md\n@@ -1,1 +1,1 @@\n-x\n+y\n";

        var result = await runner.RunAsync(diff);

        Assert.Equal(GateStepStatus.Failed, result.BuildGate.Status);
        Assert.Contains("No owning project", result.BuildGate.Detail);
    }

    [Fact]
    public async Task RunAsync_NeverTouchesTheRealWorkingTree_IndexOrStatus_AndRemovesTheCopy()
    {
        var root = FindRepositoryRoot();
        var runner = new IsolatedUniversalGateRunner(root);
        var sourceBefore = await File.ReadAllTextAsync(Path.Combine(root, GatesSource));
        var indexBefore = HashFile(Path.Combine(root, ".git", "index"));
        var statusBefore = await GitStatusAsync(root);
        var copiesBefore = CountGateCopies();

        var passing = await runner.RunAsync(InsertAtTop(root, GatesSource, "// ADR-009 isolation probe"));
        var failing = await runner.RunAsync(InsertAtTop(root, GatesSource, "this is not C#"));

        Assert.Equal(GateStepStatus.Passed, passing.BuildGate.Status);
        Assert.Equal(GateStepStatus.Failed, failing.BuildGate.Status);
        Assert.Equal(sourceBefore, await File.ReadAllTextAsync(Path.Combine(root, GatesSource)));
        Assert.False(File.Exists(Path.Combine(root, "tests", "EOS.Gates.Tests", "GateProbeTests.cs")));
        Assert.Equal(indexBefore, HashFile(Path.Combine(root, ".git", "index")));
        Assert.Equal(statusBefore, await GitStatusAsync(root));
        Assert.Equal(copiesBefore, CountGateCopies());
    }

    // ------------------------------------------------------------------------------------
    // Toolchain failure modes: stubbed `dotnet` on a minimal fake workspace (fail closed).
    // ------------------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_FailsGate1_WhenTheBuildTimesOut_AndRemovesTheCopy()
    {
        var root = CreateFakeWorkspace();
        var marker = Path.Combine(_scratch, "started");
        var stub = CreateDotnetStub($"#!/bin/sh\n/usr/bin/touch '{marker}'\nexec /usr/bin/sleep 30\n");
        var runner = new IsolatedUniversalGateRunner(root, stepTimeout: TimeSpan.FromSeconds(2), dotnetExecutable: stub);
        var copiesBefore = CountGateCopies();

        var result = await runner.RunAsync(FakeDiff());

        Assert.True(File.Exists(marker));
        Assert.Equal(GateStepStatus.Failed, result.BuildGate.Status);
        Assert.Contains("timed out", result.BuildGate.Detail);
        Assert.Equal(GateStepStatus.NotApplicable, result.TestGate.Status);
        Assert.Equal(copiesBefore, CountGateCopies());
    }

    [Fact]
    public async Task RunAsync_FailsGate1_WhenTheToolchainIsMissing()
    {
        var root = CreateFakeWorkspace();
        var missing = Path.Combine(_scratch, "no-such-dotnet");
        var runner = new IsolatedUniversalGateRunner(root, dotnetExecutable: missing);

        var result = await runner.RunAsync(FakeDiff());

        Assert.Equal(GateStepStatus.Failed, result.BuildGate.Status);
        Assert.Contains("could not start", result.BuildGate.Detail);
        Assert.Contains("no-such-dotnet", result.BuildGate.Detail);
    }

    [Fact]
    public async Task RunAsync_FailsGate1_WhenTheBuildExitsNonZero_WithBoundedOutput()
    {
        var root = CreateFakeWorkspace();
        var stub = CreateDotnetStub("#!/bin/sh\ni=0\nwhile [ $i -lt 400 ]; do echo \"line $i of a very long build log that must be bounded\"; i=$((i+1)); done\necho 'error CS9999: stub failure' >&2\nexit 1\n");
        var runner = new IsolatedUniversalGateRunner(root, dotnetExecutable: stub);

        var result = await runner.RunAsync(FakeDiff());

        Assert.Equal(GateStepStatus.Failed, result.BuildGate.Status);
        Assert.Contains("CS9999", result.BuildGate.Detail);
        Assert.True(result.BuildGate.Detail!.Length <= 4_100, $"detail length {result.BuildGate.Detail.Length}");
    }

    [Fact]
    public async Task RunAsync_KillsTheChild_RemovesTheCopy_AndPropagates_WhenCancelled()
    {
        var root = CreateFakeWorkspace();
        var marker = Path.Combine(_scratch, "started");
        var stub = CreateDotnetStub($"#!/bin/sh\n/usr/bin/touch '{marker}'\nexec /usr/bin/sleep 30\n");
        var runner = new IsolatedUniversalGateRunner(root, stepTimeout: TimeSpan.FromMinutes(5), dotnetExecutable: stub);
        using var cancellation = new CancellationTokenSource();
        var copiesBefore = CountGateCopies();

        var run = runner.RunAsync(FakeDiff(), cancellation.Token);
        var waited = 0;
        while (!File.Exists(marker) && waited++ < 200)
        {
            await Task.Delay(50);
        }

        Assert.True(File.Exists(marker), "the stubbed build never started");

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(copiesBefore, CountGateCopies());
    }

    // ------------------------------------------------------------------------------------
    // Helpers.
    // ------------------------------------------------------------------------------------

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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EOS.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root (EOS.slnx not found).");
    }

    /// <summary>A unified diff inserting <paramref name="newLine"/> before the first line of an existing file (three lines of context).</summary>
    private static string InsertAtTop(string root, string relativePath, string newLine)
    {
        var lines = File.ReadAllLines(Path.Combine(root, relativePath)).Take(3).ToArray();
        return $"--- a/{relativePath}\n+++ b/{relativePath}\n@@ -1,{lines.Length} +1,{lines.Length + 1} @@\n+{newLine}\n"
            + string.Concat(lines.Select(l => $" {l}\n"));
    }

    private static string CreateFile(string relativePath, string content)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return $"--- /dev/null\n+++ b/{relativePath}\n@@ -0,0 +1,{lines.Length} @@\n" + string.Concat(lines.Select(l => $"+{l}\n"));
    }

    private static string ProbeTest(string body) =>
        "namespace EOS.Gates.Tests;\n\npublic class GateProbeTests\n{\n    [Fact]\n    public void Probe()\n    {\n        " + body + "\n    }\n}\n";

    private static string HashFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task<string> GitStatusAsync(string root)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("status");
        info.ArgumentList.Add("--porcelain");
        using var process = System.Diagnostics.Process.Start(info)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }

    private static int CountGateCopies() => Directory.GetDirectories(Path.GetTempPath(), "eos-gate-*").Length;

    private string CreateFakeWorkspace()
    {
        var root = Path.Combine(_scratch, "fake-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "src", "EOS.Fake"));
        File.WriteAllText(Path.Combine(root, "src", "EOS.Fake", "EOS.Fake.csproj"), "<Project />\n");
        File.WriteAllText(Path.Combine(root, "src", "EOS.Fake", "Fake.cs"), "line one\nline two\nline three\n");
        return root;
    }

    private static string FakeDiff() =>
        "--- a/src/EOS.Fake/Fake.cs\n+++ b/src/EOS.Fake/Fake.cs\n@@ -1,3 +1,4 @@\n+inserted\n line one\n line two\n line three\n";

    /// <summary>
    /// A stub <c>dotnet</c> script, injected by absolute path: the runner resolves a bare
    /// <c>dotnet</c> through the host's process-path rules (the test host's own directory before
    /// PATH), so PATH replacement cannot redirect it the way WorkspaceReaderTests redirects git.
    /// </summary>
    private string CreateDotnetStub(string script)
    {
        var stub = Path.Combine(_scratch, "dotnet-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(stub, script);
        File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return stub;
    }
}
