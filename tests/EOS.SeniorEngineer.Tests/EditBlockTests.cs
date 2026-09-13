using EOS.SeniorEngineer;

namespace EOS.SeniorEngineer.Tests;

// ADR-008: exact edit blocks — parsing, strict validation, deterministic in-memory application.
public class EditBlockTests
{
    private const string Src = "src/EOS.Web/DashboardWebHost.cs";
    private const string Tst = "tests/EOS.Web.Tests/DashboardWebHostTests.cs";
    private const string SrcContent = "namespace EOS.Web;\n\npublic static class Host\n{\n    public static void Map()\n    {\n    }\n}\n";

    private static string Block(string path, string search, string replace) =>
        $"[EDIT]\nFILE: {path}\nSEARCH:\n{search}\nREPLACE:\n{replace}\n[/EDIT]";

    private static IReadOnlyList<(string Path, string? Content)> Files(string? src = SrcContent, string? tst = null) =>
        [(Src, src), (Tst, tst)];

    [Fact]
    public void ExtractEditBlocks_ParsesASingleBlock()
    {
        var blocks = SeniorEngineer.ExtractEditBlocks(Block(Src, "    public static void Map()", "    public static void Map()\n    // added"));

        var block = Assert.Single(blocks);
        Assert.Equal(Src, block.Path);
        Assert.Equal("    public static void Map()", block.Search);
        Assert.Equal("    public static void Map()\n    // added", block.Replace);
    }

    [Fact]
    public void ExtractEditBlocks_ParsesMultipleBlocksInOrder_AndIgnoresTextOutsideBlocks()
    {
        var text = "Sure, here are the edits:\n" + Block(Src, "a", "b") + "\nand\n" + Block(Tst, "", "new file") + "\nDone.";

        var blocks = SeniorEngineer.ExtractEditBlocks(text);

        Assert.Equal(2, blocks.Count);
        Assert.Equal(Src, blocks[0].Path);
        Assert.Equal(Tst, blocks[1].Path);
        Assert.Equal(string.Empty, blocks[1].Search);
    }

    [Theory]
    [InlineData("[EDIT]\nFILE: src/x.cs\nSEARCH:\na\nREPLACE:\nb\n")]                 // unterminated
    [InlineData("[EDIT]\nSEARCH:\na\nREPLACE:\nb\n[/EDIT]")]                          // no FILE
    [InlineData("[EDIT]\nFILE: src/x.cs\nREPLACE:\nb\n[/EDIT]")]                      // no SEARCH
    [InlineData("[EDIT]\nFILE: src/x.cs\nSEARCH:\na\n[/EDIT]")]                       // no REPLACE
    [InlineData("--- a/src/x.cs\n+++ b/src/x.cs\n@@ -1 +1 @@\n-a\n+b\n")]              // a diff, not blocks
    [InlineData("I would add a /health endpoint.")]                                    // prose only
    [InlineData("")]
    public void ExtractEditBlocks_Throws_ForMalformedOrAbsentBlocks(string text)
    {
        Assert.Throws<InvalidOperationException>(() => SeniorEngineer.ExtractEditBlocks(text));
    }

    [Fact]
    public void ApplyEditBlocks_AppliesAValidSingleEdit_InMemoryOnly()
    {
        var blocks = new[] { new SeniorEngineer.EditBlock(Src, "    public static void Map()\n    {\n    }", "    public static void Map()\n    {\n        // health\n    }") };

        var result = SeniorEngineer.ApplyEditBlocks(blocks, Files());

        Assert.Equal("namespace EOS.Web;\n\npublic static class Host\n{\n    public static void Map()\n    {\n        // health\n    }\n}\n", result[Src]);
        Assert.Null(result[Tst]);
    }

    [Fact]
    public void ApplyEditBlocks_AppliesMultipleEditsDeterministically_InSuppliedOrder()
    {
        var blocks = new[]
        {
            new SeniorEngineer.EditBlock(Src, "namespace EOS.Web;", "namespace EOS.Web;\nusing System;"),
            new SeniorEngineer.EditBlock(Src, "    {\n    }", "    {\n        // second\n    }"),
        };

        var first = SeniorEngineer.ApplyEditBlocks(blocks, Files());
        var second = SeniorEngineer.ApplyEditBlocks(blocks, Files());

        Assert.Equal(first[Src], second[Src]);
        Assert.StartsWith("namespace EOS.Web;\nusing System;\n", first[Src]);
        Assert.Contains("        // second\n", first[Src]);
    }

    [Theory]
    [InlineData("src/EOS.Runner/Program.cs")]      // in-root but not referenced
    [InlineData("config/Security.json")]
    [InlineData("src/../config/Security.json")]
    [InlineData("/etc/passwd")]
    [InlineData("src\\EOS.Web\\DashboardWebHost.cs")]
    public void ApplyEditBlocks_Throws_ForAnUnknownOrForbiddenPath(string path)
    {
        var blocks = new[] { new SeniorEngineer.EditBlock(path, "namespace EOS.Web;", "x") };

        var exception = Assert.Throws<InvalidOperationException>(() => SeniorEngineer.ApplyEditBlocks(blocks, Files()));

        Assert.Contains("not one of the referenced files", exception.Message);
    }

    [Fact]
    public void ApplyEditBlocks_Throws_WhenSearchOccursZeroTimes()
    {
        var blocks = new[] { new SeniorEngineer.EditBlock(Src, "namespace EOS.Fabricated;", "x") };

        var exception = Assert.Throws<InvalidOperationException>(() => SeniorEngineer.ApplyEditBlocks(blocks, Files()));

        Assert.Contains("was not found verbatim", exception.Message);
    }

    [Fact]
    public void ApplyEditBlocks_Throws_WhenSearchOccursTwice()
    {
        var content = "using A;\nusing B;\nusing A;\n";
        var blocks = new[] { new SeniorEngineer.EditBlock(Src, "using A;", "using C;") };

        var exception = Assert.Throws<InvalidOperationException>(() => SeniorEngineer.ApplyEditBlocks(blocks, Files(src: content)));

        Assert.Contains("occurs 2 times", exception.Message);
    }

    [Theory]
    [InlineData("public static void Map()")]           // dedented by one level
    [InlineData("    public static void Map() ")]      // trailing space added
    [InlineData("    PUBLIC static void Map()")]       // case differs
    public void ApplyEditBlocks_Throws_OnAnyWhitespaceOrCaseMismatch_NoNormalization(string search)
    {
        var blocks = new[] { new SeniorEngineer.EditBlock(Src, search, "x") };

        Assert.Throws<InvalidOperationException>(() => SeniorEngineer.ApplyEditBlocks(blocks, Files()));
    }

    [Fact]
    public void ApplyEditBlocks_Throws_WhenSearchMatchesOnlyPartOfALine()
    {
        var blocks = new[] { new SeniorEngineer.EditBlock(Src, "EOS.Web", "EOS.Other") };

        Assert.Throws<InvalidOperationException>(() => SeniorEngineer.ApplyEditBlocks(blocks, Files()));
    }

    [Fact]
    public void ApplyEditBlocks_CreatesAMissingFile_FromAnEmptySearch_WithATrailingNewline()
    {
        var blocks = new[] { new SeniorEngineer.EditBlock(Tst, string.Empty, "namespace EOS.Web.Tests;\n\npublic class HealthTests { }") };

        var result = SeniorEngineer.ApplyEditBlocks(blocks, Files());

        Assert.Equal("namespace EOS.Web.Tests;\n\npublic class HealthTests { }\n", result[Tst]);
        Assert.Equal(SrcContent, result[Src]);
    }

    [Fact]
    public void ApplyEditBlocks_Throws_ForAnEmptySearchOnAnExistingFile()
    {
        var blocks = new[] { new SeniorEngineer.EditBlock(Src, string.Empty, "replacement") };

        var exception = Assert.Throws<InvalidOperationException>(() => SeniorEngineer.ApplyEditBlocks(blocks, Files()));

        Assert.Contains("empty SEARCH", exception.Message);
    }

    [Fact]
    public void ApplyEditBlocks_Throws_ForANonEmptySearchOnAMissingFile()
    {
        var blocks = new[] { new SeniorEngineer.EditBlock(Tst, "something", "replacement") };

        Assert.Throws<InvalidOperationException>(() => SeniorEngineer.ApplyEditBlocks(blocks, Files()));
    }

    [Fact]
    public void ApplyEditBlocks_Throws_ForAnEmptyReplaceOnCreation()
    {
        var blocks = new[] { new SeniorEngineer.EditBlock(Tst, string.Empty, string.Empty) };

        Assert.Throws<InvalidOperationException>(() => SeniorEngineer.ApplyEditBlocks(blocks, Files()));
    }

    // A later block whose SEARCH was consumed/duplicated by an earlier edit fails closed.
    [Fact]
    public void ApplyEditBlocks_FailsClosed_WhenAnEarlierEditMakesALaterSearchAmbiguousOrAbsent()
    {
        var duplicate = new[]
        {
            new SeniorEngineer.EditBlock(Src, "    public static void Map()", "    public static void Map()\n    public static void Map()"),
            new SeniorEngineer.EditBlock(Src, "    public static void Map()", "x"),
        };
        var removed = new[]
        {
            new SeniorEngineer.EditBlock(Src, "namespace EOS.Web;", "namespace EOS.Other;"),
            new SeniorEngineer.EditBlock(Src, "namespace EOS.Web;", "x"),
        };

        Assert.Throws<InvalidOperationException>(() => SeniorEngineer.ApplyEditBlocks(duplicate, Files()));
        Assert.Throws<InvalidOperationException>(() => SeniorEngineer.ApplyEditBlocks(removed, Files()));
    }
}
