using EOS.SeniorEngineer;

namespace EOS.SeniorEngineer.Tests;

// Post-Roadmap WP-A: both sides of every path in a produced diff are validated against the
// permitted roots AND the human-referenced path set. Deterministic, no patch-parser framework.
public class DiffValidationTests
{
    private static readonly string[] Referenced =
    [
        "src/EOS.Web/DashboardWebHost.cs",
        "tests/EOS.Web.Tests/DashboardWebHostTests.cs",
    ];

    private static string Modify(string oldPath, string newPath) =>
        $"""
        diff --git a/{oldPath} b/{newPath}
        --- a/{oldPath}
        +++ b/{newPath}
        @@ -1,1 +1,2 @@
         existing
        +added

        """;

    [Fact]
    public void Accepts_AValidSrcPath_OnBothSides()
    {
        Assert.Null(SeniorEngineer.ValidateUnifiedDiff(Modify(Referenced[0], Referenced[0]), Referenced));
    }

    [Fact]
    public void Accepts_AValidTestsPath_OnBothSides()
    {
        Assert.Null(SeniorEngineer.ValidateUnifiedDiff(Modify(Referenced[1], Referenced[1]), Referenced));
    }

    [Fact]
    public void Accepts_ADiffWithoutTheOptionalGitHeaderLine()
    {
        var diff = $"""
            --- a/{Referenced[0]}
            +++ b/{Referenced[0]}
            @@ -1 +1 @@
            -old
            +new

            """;

        Assert.Null(SeniorEngineer.ValidateUnifiedDiff(diff, Referenced));
    }

    [Fact]
    public void Rejects_AnOutOfScopeOldPath_EvenWhenTheNewPathIsValid()
    {
        var diff = $"""
            --- a/config/Security.json
            +++ b/{Referenced[0]}
            @@ -1 +1 @@
            -old
            +new

            """;

        Assert.Contains("old path", SeniorEngineer.ValidateUnifiedDiff(diff, Referenced));
    }

    [Fact]
    public void Rejects_AnOutOfScopeNewPath_EvenWhenTheOldPathIsValid()
    {
        var diff = $"""
            --- a/{Referenced[0]}
            +++ b/docs/EOS-Specification.md
            @@ -1 +1 @@
            -old
            +new

            """;

        Assert.Contains("new path", SeniorEngineer.ValidateUnifiedDiff(diff, Referenced));
    }

    [Theory]
    [InlineData("src/../config/Security.json")]
    [InlineData("src/EOS.Web/../../.env")]
    [InlineData("src/./EOS.Web/DashboardWebHost.cs")]
    [InlineData("src//EOS.Web/DashboardWebHost.cs")]
    public void Rejects_TraversalAndDegenerateSegments(string path)
    {
        var diff = $"""
            --- a/{path}
            +++ b/{path}
            @@ -1 +1 @@
            -old
            +new

            """;

        Assert.NotNull(SeniorEngineer.ValidateUnifiedDiff(diff, [.. Referenced, path]));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("src\\EOS.Web\\DashboardWebHost.cs")]
    public void Rejects_AbsoluteDriveLetterAndBackslashPaths(string path)
    {
        var diff = $"""
            --- a/{path}
            +++ b/{path}
            @@ -1 +1 @@
            -old
            +new

            """;

        Assert.NotNull(SeniorEngineer.ValidateUnifiedDiff(diff, [.. Referenced, path]));
    }

    [Fact]
    public void Accepts_DevNullAsTheOldSide_ForFileCreation()
    {
        var diff = $"""
            diff --git a/{Referenced[1]} b/{Referenced[1]}
            --- /dev/null
            +++ b/{Referenced[1]}
            @@ -0,0 +1,2 @@
            +namespace EOS.Web.Tests;
            +// health tests

            """;

        Assert.Null(SeniorEngineer.ValidateUnifiedDiff(diff, Referenced));
    }

    [Fact]
    public void Accepts_DevNullAsTheNewSide_ForFileDeletion()
    {
        var diff = $"""
            --- a/{Referenced[1]}
            +++ /dev/null
            @@ -1,1 +0,0 @@
            -gone

            """;

        Assert.Null(SeniorEngineer.ValidateUnifiedDiff(diff, Referenced));
    }

    [Fact]
    public void Rejects_DevNullOnBothSides()
    {
        var diff = """
            --- /dev/null
            +++ /dev/null
            @@ -0,0 +0,0 @@

            """;

        Assert.Contains("/dev/null", SeniorEngineer.ValidateUnifiedDiff(diff, Referenced));
    }

    [Fact]
    public void Rejects_ARenameToAnOutOfScopePath()
    {
        var diff = $"""
            diff --git a/{Referenced[0]} b/{Referenced[0]}
            rename from {Referenced[0]}
            rename to config/Thresholds.json
            --- a/{Referenced[0]}
            +++ b/{Referenced[0]}
            @@ -1 +1 @@
            -old
            +new

            """;

        Assert.Contains("rename to", SeniorEngineer.ValidateUnifiedDiff(diff, Referenced));
    }

    [Fact]
    public void Rejects_ACopyFromAnOutOfScopePath()
    {
        var diff = $"""
            diff --git a/{Referenced[0]} b/{Referenced[0]}
            copy from docs/EOS-Specification.md
            copy to {Referenced[0]}
            --- a/{Referenced[0]}
            +++ b/{Referenced[0]}
            @@ -1 +1 @@
            -old
            +new

            """;

        Assert.Contains("copy from", SeniorEngineer.ValidateUnifiedDiff(diff, Referenced));
    }

    [Fact]
    public void Rejects_AGitHeaderLineWhoseNewPathIsOutOfScope()
    {
        var diff = $"""
            diff --git a/{Referenced[0]} b/deploy/backup.sh
            --- a/{Referenced[0]}
            +++ b/{Referenced[0]}
            @@ -1 +1 @@
            -old
            +new

            """;

        Assert.Contains("diff --git new path", SeniorEngineer.ValidateUnifiedDiff(diff, Referenced));
    }

    [Fact]
    public void Rejects_AnInRootPathTheTaskDidNotReference()
    {
        var unreferenced = "src/EOS.Runner/Program.cs";

        var violation = SeniorEngineer.ValidateUnifiedDiff(Modify(unreferenced, unreferenced), Referenced);

        Assert.Contains("was not referenced by the task", violation);
    }

    [Fact]
    public void Rejects_AMissingPrefix()
    {
        var diff = $"""
            --- {Referenced[0]}
            +++ b/{Referenced[0]}
            @@ -1 +1 @@
            -old
            +new

            """;

        Assert.Contains("a/ prefix", SeniorEngineer.ValidateUnifiedDiff(diff, Referenced));
    }

    [Fact]
    public void Rejects_AMissingPlusPlusPlusLine()
    {
        var diff = $"""
            --- a/{Referenced[0]}
            @@ -1 +1 @@
            -old
            +new

            """;

        Assert.Contains("not immediately followed by a +++", SeniorEngineer.ValidateUnifiedDiff(diff, Referenced));
    }

    [Fact]
    public void Rejects_AMissingHunk()
    {
        var diff = $"""
            --- a/{Referenced[0]}
            +++ b/{Referenced[0]}
            -old
            +new

            """;

        Assert.Contains("no @@ hunk", SeniorEngineer.ValidateUnifiedDiff(diff, Referenced));
    }

    [Fact]
    public void Rejects_ProseWithoutAnyHeaderPair()
    {
        Assert.Contains("no ---/+++", SeniorEngineer.ValidateUnifiedDiff("Here is what I would change: add an endpoint.\n", Referenced));
    }

    [Fact]
    public void ExtractDiffFence_ReturnsNull_WhenThereIsNoFence()
    {
        Assert.Null(SeniorEngineer.ExtractDiffFence("Here is an explanation instead of a diff."));
        Assert.Null(SeniorEngineer.ExtractDiffFence("```diff\n--- a/x\n+++ b/x\n(unterminated)"));
    }

    [Fact]
    public void ExtractDiffFence_ReturnsTheBodyOfTheFirstDiffFence()
    {
        var text = "Sure.\n```diff\n--- a/src/x.cs\n+++ b/src/x.cs\n@@ -1 +1 @@\n-a\n+b\n```\nDone.";

        Assert.Equal("--- a/src/x.cs\n+++ b/src/x.cs\n@@ -1 +1 @@\n-a\n+b\n", SeniorEngineer.ExtractDiffFence(text));
    }
}
