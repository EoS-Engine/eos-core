using System.Diagnostics;
using System.Runtime.Versioning;
using EOS.SeniorEngineer;

namespace EOS.SeniorEngineer.Tests;

// ADR-008: the diff is a deterministic function of (original, modified). Every generated diff is
// round-tripped through a real `git apply --check` in an isolated temporary git repository — the
// builder is not correct because its output "looks like" a diff, but because git accepts it.
[SupportedOSPlatform("linux")]
public class UnifiedDiffBuilderTests
{
    private const string Path1 = "src/EOS.Web/DashboardWebHost.cs";

    private static readonly string Original = "line1\nline2\nline3\nline4\nline5\nline6\nline7\nline8\nline9\nline10\n";

    [Fact]
    public void Build_ReturnsNull_WhenContentIsUnchanged()
    {
        Assert.Null(UnifiedDiffBuilder.Build(Path1, Original, Original));
    }

    [Fact]
    public void Build_OneLineModification_HasCorrectHeaderCountsOffsetsAndContext()
    {
        var modified = Original.Replace("line5\n", "LINE5\n");

        var diff = UnifiedDiffBuilder.Build(Path1, Original, modified)!;

        Assert.Equal(
            "--- a/src/EOS.Web/DashboardWebHost.cs\n+++ b/src/EOS.Web/DashboardWebHost.cs\n@@ -2,7 +2,7 @@\n line2\n line3\n line4\n-line5\n+LINE5\n line6\n line7\n line8\n",
            diff);
        AssertAppliesWithGit(Original, diff);
    }

    [Fact]
    public void Build_Insertion_AtEndAndMiddle()
    {
        var middle = Original.Replace("line5\n", "line5\ninserted\n");
        var end = Original + "line11\n";

        var middleDiff = UnifiedDiffBuilder.Build(Path1, Original, middle)!;
        var endDiff = UnifiedDiffBuilder.Build(Path1, Original, end)!;

        Assert.Contains("@@ -3,6 +3,7 @@\n line3\n line4\n line5\n+inserted\n line6\n line7\n line8\n", middleDiff);
        Assert.Contains("@@ -8,3 +8,4 @@\n line8\n line9\n line10\n+line11\n", endDiff);
        AssertAppliesWithGit(Original, middleDiff);
        AssertAppliesWithGit(Original, endDiff);
    }

    [Fact]
    public void Build_Deletion_AtStartAndMiddle()
    {
        var start = Original.Replace("line1\n", string.Empty);
        var middle = Original.Replace("line6\n", string.Empty);

        var startDiff = UnifiedDiffBuilder.Build(Path1, Original, start)!;
        var middleDiff = UnifiedDiffBuilder.Build(Path1, Original, middle)!;

        Assert.Contains("@@ -1,4 +1,3 @@\n-line1\n line2\n line3\n line4\n", startDiff);
        Assert.Contains("@@ -3,7 +3,6 @@\n line3\n line4\n line5\n-line6\n line7\n line8\n line9\n", middleDiff);
        AssertAppliesWithGit(Original, startDiff);
        AssertAppliesWithGit(Original, middleDiff);
    }

    [Fact]
    public void Build_MultiLineModification()
    {
        var modified = Original.Replace("line4\nline5\nline6\n", "four\nfive\nfive-and-a-half\nsix\n");

        var diff = UnifiedDiffBuilder.Build(Path1, Original, modified)!;

        Assert.Contains("@@ -1,9 +1,10 @@\n", diff);
        Assert.Contains("-line4\n-line5\n-line6\n+four\n+five\n+five-and-a-half\n+six\n", diff);
        AssertAppliesWithGit(Original, diff);
    }

    [Fact]
    public void Build_MultipleHunks_WhenChangesAreFarApart()
    {
        var original = string.Concat(Enumerable.Range(1, 30).Select(i => $"line{i}\n"));
        var modified = original.Replace("line3\n", "LINE3\n").Replace("line25\n", "LINE25\n");

        var diff = UnifiedDiffBuilder.Build(Path1, original, modified)!;

        Assert.Equal(2, diff.Split("@@ -").Length - 1);
        Assert.Contains("@@ -1,6 +1,6 @@\n", diff);
        Assert.Contains("@@ -22,7 +22,7 @@\n", diff);
        AssertAppliesWithGit(original, diff);
    }

    [Fact]
    public void Build_MergesNearbyChanges_IntoOneHunk()
    {
        var modified = Original.Replace("line3\n", "LINE3\n").Replace("line8\n", "LINE8\n");

        var diff = UnifiedDiffBuilder.Build(Path1, Original, modified)!;

        Assert.Equal(1, diff.Split("@@ -").Length - 1);
        AssertAppliesWithGit(Original, diff);
    }

    [Fact]
    public void Build_NewFile_UsesDevNullOldSide()
    {
        var diff = UnifiedDiffBuilder.Build(Path1, original: null, modified: "namespace EOS.Web;\n\npublic class New { }\n")!;

        Assert.Equal(
            "--- /dev/null\n+++ b/src/EOS.Web/DashboardWebHost.cs\n@@ -0,0 +1,3 @@\n+namespace EOS.Web;\n+\n+public class New { }\n",
            diff);
        AssertAppliesWithGit(original: null, diff);
    }

    [Fact]
    public void Build_DeletedFile_UsesDevNullNewSide()
    {
        var diff = UnifiedDiffBuilder.Build(Path1, "a\nb\n", modified: null)!;

        Assert.Equal("--- a/src/EOS.Web/DashboardWebHost.cs\n+++ /dev/null\n@@ -1,2 +0,0 @@\n-a\n-b\n", diff);
        AssertAppliesWithGit("a\nb\n", diff);
    }

    [Fact]
    public void Build_PreservesTrailingNewlineSemantics_AndEmitsTheNoNewlineMarker()
    {
        var withoutNewline = "line1\nline2";
        var withNewline = "line1\nline2\n";

        var addNewline = UnifiedDiffBuilder.Build(Path1, withoutNewline, withNewline)!;
        var removeNewline = UnifiedDiffBuilder.Build(Path1, withNewline, withoutNewline)!;
        var editLastNoNewline = UnifiedDiffBuilder.Build(Path1, withoutNewline, "line1\nLINE2")!;

        Assert.Equal("--- a/src/EOS.Web/DashboardWebHost.cs\n+++ b/src/EOS.Web/DashboardWebHost.cs\n@@ -1,2 +1,2 @@\n line1\n-line2\n\\ No newline at end of file\n+line2\n", addNewline);
        Assert.Equal("--- a/src/EOS.Web/DashboardWebHost.cs\n+++ b/src/EOS.Web/DashboardWebHost.cs\n@@ -1,2 +1,2 @@\n line1\n-line2\n+line2\n\\ No newline at end of file\n", removeNewline);
        Assert.Contains("-line2\n\\ No newline at end of file\n+LINE2\n\\ No newline at end of file\n", editLastNoNewline);
        AssertAppliesWithGit(withoutNewline, addNewline);
        AssertAppliesWithGit(withNewline, removeNewline);
        AssertAppliesWithGit(withoutNewline, editLastNoNewline);
    }

    [Fact]
    public void Build_EmptyFileEdgeCases()
    {
        var fromEmpty = UnifiedDiffBuilder.Build(Path1, string.Empty, "x\n")!;
        var toEmpty = UnifiedDiffBuilder.Build(Path1, "x\n", string.Empty)!;

        Assert.Contains("@@ -0,0 +1,1 @@\n+x\n", fromEmpty);
        Assert.Contains("@@ -1,1 +0,0 @@\n-x\n", toEmpty);
        AssertAppliesWithGit(string.Empty, fromEmpty);
        AssertAppliesWithGit("x\n", toEmpty);
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        var modified = Original.Replace("line2\n", "two\n").Replace("line9\n", "nine\nnine-b\n");

        var first = UnifiedDiffBuilder.Build(Path1, Original, modified);
        var second = UnifiedDiffBuilder.Build(Path1, Original, modified);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Build_RoundTrips_AgainstTheRealDemoFile()
    {
        var original = File.ReadAllText(System.IO.Path.Combine(FindRepositoryRoot(), "src", "EOS.Web", "DashboardWebHost.cs"));
        var anchor = "        app.MapGet(\"/\", () => Results.Content(BuildHtml(dashboardOptions.Title), \"text/html\"));\n";
        Assert.Contains(anchor, original);
        var modified = original.Replace(anchor, anchor + "\n        app.MapGet(\"/health\", () => Results.Text(\"OK\"));\n");

        var diff = UnifiedDiffBuilder.Build(Path1, original, modified)!;

        Assert.Null(SeniorEngineer.ValidateUnifiedDiff(diff, [Path1]));
        AssertAppliesWithGit(original, diff);
    }

    [Fact]
    public void Build_Throws_WhenBothSidesAreAbsent()
    {
        Assert.Throws<ArgumentException>(() => UnifiedDiffBuilder.Build(Path1, null, null));
    }

    // Round-trip proof: the diff is fed on stdin to a real `git apply --check` inside an isolated
    // temporary git repository containing the original file (or nothing, for a creation).
    private static void AssertAppliesWithGit(string? original, string diff)
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"eos-udb-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(System.IO.Path.Combine(root, "src", "EOS.Web"));
            RunGit(root, ["init", "-q"]);
            if (original is not null)
            {
                File.WriteAllText(System.IO.Path.Combine(root, "src", "EOS.Web", "DashboardWebHost.cs"), original);
            }

            var (exitCode, stderr) = RunGit(root, ["apply", "--check", "--"], diff);

            Assert.True(exitCode == 0, $"git apply --check rejected the generated diff:\n{stderr}\n--- diff ---\n{diff}");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (int ExitCode, string StdErr) RunGit(string root, string[] args, string? stdin = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
        }

        process.StandardInput.Close();
        var stderr = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stderr);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "EOS.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root (EOS.slnx not found).");
    }
}
