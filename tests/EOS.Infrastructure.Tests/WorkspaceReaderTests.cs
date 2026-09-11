using System.Runtime.Versioning;
using EOS.Infrastructure;

namespace EOS.Infrastructure.Tests;

// Post-Roadmap WP-A: the read-only, root-bound workspace boundary (Protection §11 "Local Files").
// The applicability-check stubs below are POSIX shell scripts with Unix file modes (the same
// Linux-only declaration EOS.RestoreDrill.Tests makes at assembly level), scoped to this class.
[SupportedOSPlatform("linux")]
public class WorkspaceReaderTests : IDisposable
{
    private readonly string _root;

    public WorkspaceReaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"eos-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, "src", "EOS.Web"));
        Directory.CreateDirectory(Path.Combine(_root, "tests", "EOS.Web.Tests"));
        Directory.CreateDirectory(Path.Combine(_root, "config"));
        File.WriteAllText(Path.Combine(_root, "src", "EOS.Web", "DashboardWebHost.cs"), "namespace EOS.Web;\n");
        File.WriteAllText(Path.Combine(_root, "config", "Security.json"), "{}");
        File.WriteAllText(Path.Combine(_root, ".env"), "SECRET=1");
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task ReadFileAsync_ReadsAnExistingFileUnderSrc()
    {
        var reader = new WorkspaceReader(_root);

        var content = await reader.ReadFileAsync("src/EOS.Web/DashboardWebHost.cs");

        Assert.Equal("namespace EOS.Web;\n", content);
    }

    [Fact]
    public async Task ReadFileAsync_ReturnsNull_ForAMissingFileUnderAPermittedRoot()
    {
        var reader = new WorkspaceReader(_root);

        Assert.Null(await reader.ReadFileAsync("tests/EOS.Web.Tests/DashboardWebHostTests.cs"));
    }

    [Theory]
    [InlineData("config/Security.json")]
    [InlineData(".env")]
    [InlineData("docs/EOS-Specification.md")]
    [InlineData("EOS.slnx")]
    [InlineData("src/../config/Security.json")]
    [InlineData("src/./EOS.Web/DashboardWebHost.cs")]
    [InlineData("src//EOS.Web/DashboardWebHost.cs")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("src\\EOS.Web\\DashboardWebHost.cs")]
    [InlineData("src")]
    [InlineData("")]
    public async Task ReadFileAsync_Rejects_PathsOutsideThePermittedRoots(string relativePath)
    {
        var reader = new WorkspaceReader(_root);

        await Assert.ThrowsAsync<ArgumentException>(() => reader.ReadFileAsync(relativePath));
    }

    // ---------------------------------------------------------------------------------------
    // ADR-007: CheckPatchAppliesAsync — real `git apply --check` against the temp root (works
    // outside a git repository, exactly like production inside one), read-only, fail-closed.
    // ---------------------------------------------------------------------------------------

    private const string Src = "src/EOS.Web/DashboardWebHost.cs";
    private const string Tst = "tests/EOS.Web.Tests/DashboardWebHostTests.cs";

    private static string Modify(string path, string oldLine, string newLine) =>
        $"--- a/{path}\n+++ b/{path}\n@@ -1 +1 @@\n-{oldLine}\n+{newLine}\n";

    private string Snapshot() => string.Join("|", Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
        .OrderBy(f => f, StringComparer.Ordinal).Select(f => $"{Path.GetRelativePath(_root, f)}={File.ReadAllText(f).Length}"));

    [Fact]
    public async Task CheckPatchAppliesAsync_ReportsApplicable_ForAMatchingModification_AndLeavesTheWorkspaceUntouched()
    {
        var reader = new WorkspaceReader(_root);
        var before = Snapshot();

        var result = await reader.CheckPatchAppliesAsync(Modify(Src, "namespace EOS.Web;", "namespace EOS.Web; // health"));

        Assert.True(result.Applies, result.Error);
        Assert.Null(result.Error);
        Assert.Equal(before, Snapshot());
        Assert.False(Directory.Exists(Path.Combine(_root, ".git")));
        Assert.Equal("namespace EOS.Web;\n", File.ReadAllText(Path.Combine(_root, "src", "EOS.Web", "DashboardWebHost.cs")));
    }

    [Fact]
    public async Task CheckPatchAppliesAsync_ReportsNotApplicable_ForFabricatedHunkContext()
    {
        var reader = new WorkspaceReader(_root);

        var result = await reader.CheckPatchAppliesAsync(Modify(Src, "namespace EOS.Fabricated;", "namespace EOS.Web;"));

        Assert.False(result.Applies);
        Assert.Contains("does not apply", result.Error);
    }

    [Fact]
    public async Task CheckPatchAppliesAsync_ReportsApplicable_ForANewFile_OnlyWhenItDoesNotExist()
    {
        var reader = new WorkspaceReader(_root);
        var createTest = $"--- /dev/null\n+++ b/{Tst}\n@@ -0,0 +1 @@\n+// health tests\n";
        var createExisting = $"--- /dev/null\n+++ b/{Src}\n@@ -0,0 +1 @@\n+namespace EOS.Web;\n";

        Assert.True((await reader.CheckPatchAppliesAsync(createTest)).Applies);
        var existing = await reader.CheckPatchAppliesAsync(createExisting);
        Assert.False(existing.Applies);
        Assert.Contains("already exists", existing.Error);
        Assert.False(File.Exists(Path.Combine(_root, "tests", "EOS.Web.Tests", "DashboardWebHostTests.cs")));
    }

    [Fact]
    public async Task CheckPatchAppliesAsync_ReportsApplicable_ForADeletion_OnlyWhenTheContentMatches()
    {
        var reader = new WorkspaceReader(_root);
        var deleteMatching = $"--- a/{Src}\n+++ /dev/null\n@@ -1 +0,0 @@\n-namespace EOS.Web;\n";
        var deleteWrong = $"--- a/{Src}\n+++ /dev/null\n@@ -1 +0,0 @@\n-namespace EOS.Other;\n";

        Assert.True((await reader.CheckPatchAppliesAsync(deleteMatching)).Applies);
        Assert.False((await reader.CheckPatchAppliesAsync(deleteWrong)).Applies);
        Assert.True(File.Exists(Path.Combine(_root, "src", "EOS.Web", "DashboardWebHost.cs")));
    }

    [Fact]
    public async Task CheckPatchAppliesAsync_ReportsNotApplicable_WhenAnyFileOfAMultiFilePatchFails()
    {
        var reader = new WorkspaceReader(_root);
        var goodThenBad = Modify(Src, "namespace EOS.Web;", "namespace EOS.Web; // ok")
            + $"--- a/{Tst}\n+++ b/{Tst}\n@@ -1 +1 @@\n-does not exist\n+x\n";

        var result = await reader.CheckPatchAppliesAsync(goodThenBad);

        Assert.False(result.Applies);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this is not a patch\n")]
    public async Task CheckPatchAppliesAsync_ReportsNotApplicable_ForEmptyOrMalformedInput(string input)
    {
        var reader = new WorkspaceReader(_root);

        var result = await reader.CheckPatchAppliesAsync(input);

        Assert.False(result.Applies);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData("--- a/../outside.txt\n+++ b/../outside.txt\n@@ -1 +1 @@\n-a\n+b\n")]
    [InlineData("--- a//etc/passwd\n+++ b//etc/passwd\n@@ -1 +1 @@\n-a\n+b\n")]
    public async Task CheckPatchAppliesAsync_FailsClosed_ForTraversalOrAbsolutePaths(string patch)
    {
        var reader = new WorkspaceReader(_root);

        var result = await reader.CheckPatchAppliesAsync(patch);

        Assert.False(result.Applies);
    }

    [Fact]
    public async Task CheckPatchAppliesAsync_FailsClosed_WhenGitIsUnavailable()
    {
        var reader = new WorkspaceReader(_root);
        var emptyPath = Path.Combine(_root, "empty-path");
        Directory.CreateDirectory(emptyPath);

        var result = await WithPathAsync(emptyPath, () => reader.CheckPatchAppliesAsync(Modify(Src, "namespace EOS.Web;", "x")));

        Assert.False(result.Applies);
        Assert.Contains("could not start git", result.Error);
    }

    [Fact]
    public async Task CheckPatchAppliesAsync_FailsClosed_WhenGitExitsAbnormally()
    {
        var reader = new WorkspaceReader(_root);
        var stubDir = CreateGitStub("#!/bin/sh\necho boom 1>&2\nexit 7\n");

        var result = await WithPathAsync(stubDir, () => reader.CheckPatchAppliesAsync(Modify(Src, "namespace EOS.Web;", "x")));

        Assert.False(result.Applies);
        Assert.Contains("code 7", result.Error);
        Assert.Contains("boom", result.Error);
    }

    // Bounded by WorkspaceReader's own 30 s timeout — the stub never exits on its own.
    [Fact]
    public async Task CheckPatchAppliesAsync_FailsClosed_AndKillsTheProcess_OnTimeout()
    {
        var reader = new WorkspaceReader(_root);
        var marker = Path.Combine(_root, "stub-started");
        var stubDir = CreateGitStub($"#!/bin/sh\n/usr/bin/touch '{marker}'\n/usr/bin/sleep 600\n");

        var result = await WithPathAsync(stubDir, () => reader.CheckPatchAppliesAsync(Modify(Src, "namespace EOS.Web;", "x")));

        Assert.False(result.Applies);
        Assert.Contains("timed out", result.Error);
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task CheckPatchAppliesAsync_PropagatesCancellation_AndKillsTheProcess()
    {
        var reader = new WorkspaceReader(_root);
        var stubDir = CreateGitStub("#!/bin/sh\n/usr/bin/sleep 600\n");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => WithPathAsync(stubDir, () => reader.CheckPatchAppliesAsync(Modify(Src, "namespace EOS.Web;", "x"), cancellation.Token)));
    }

    private string CreateGitStub(string script)
    {
        var stubDir = Path.Combine(_root, "stub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stubDir);
        var stub = Path.Combine(stubDir, "git");
        File.WriteAllText(stub, script);
        File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return stubDir;
    }

    // This assembly disables test parallelization, so temporarily replacing PATH for one check
    // cannot affect another test.
    private static async Task<T> WithPathAsync<T>(string pathValue, Func<Task<T>> action)
    {
        var original = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", pathValue);
        try
        {
            return await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", original);
        }
    }
}
