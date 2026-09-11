using System.Text;

namespace EOS.SeniorEngineer;

/// <summary>
/// ADR-008: deterministic construction of the unified diff that is Constitution Part 6 §6.2's
/// "Implementation evidence (diff)" — from an original and a modified text only. The model never
/// produces a diff; it produces exact edit blocks (<see cref="SeniorEngineer"/>), so hunk offsets,
/// hunk counts, context markers and diff paths are a mathematical consequence of the two texts
/// and are never under model control. Pure function: no Git, no shell, no I/O, no package.
///
/// Line diff: longest-common-subsequence (dynamic programming) over lines; hunks carry 3 lines of
/// context and are merged when separated by at most 2 × 3 unchanged lines, matching git's own
/// hunk layout. A missing trailing newline is modelled as part of the last line's identity (so a
/// change in trailing-newline status is a real line change) and rendered as git's
/// <c>\ No newline at end of file</c> marker. Paths are emitted as <c>a/&lt;path&gt;</c> /
/// <c>b/&lt;path&gt;</c>, with <c>/dev/null</c> for creation and deletion.
/// </summary>
public static class UnifiedDiffBuilder
{
    private const int ContextLines = 3;
    private const string NoNewlineMarker = "\\ No newline at end of file";

    // Sentinel appended to a final line that has no trailing newline, so that "x" and "x\n" at
    // end-of-file compare as different lines (exactly git's semantics).
    private const char NoNewlineSentinel = '\0';

    /// <summary>
    /// Builds the unified diff turning <paramref name="original"/> into <paramref name="modified"/>
    /// for <paramref name="path"/>. <see langword="null"/> content means "file absent" (creation
    /// when <paramref name="original"/> is null, deletion when <paramref name="modified"/> is
    /// null). Returns <see langword="null"/> when the two contents are identical.
    /// </summary>
    public static string? Build(string path, string? original, string? modified)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (original is null && modified is null)
        {
            throw new ArgumentException("Both original and modified content are absent; nothing to diff.", nameof(modified));
        }

        if (string.Equals(original, modified, StringComparison.Ordinal))
        {
            return null;
        }

        var oldLines = original is null ? [] : SplitLines(original);
        var newLines = modified is null ? [] : SplitLines(modified);
        var ops = Diff(oldLines, newLines);

        var builder = new StringBuilder();
        builder.Append("--- ").Append(original is null ? "/dev/null" : "a/" + path).Append('\n');
        builder.Append("+++ ").Append(modified is null ? "/dev/null" : "b/" + path).Append('\n');

        foreach (var hunk in GroupIntoHunks(ops))
        {
            builder.Append(hunk);
        }

        return builder.ToString();
    }

    private static string[] SplitLines(string content)
    {
        if (content.Length == 0)
        {
            return [];
        }

        var endsWithNewline = content.EndsWith('\n');
        var body = endsWithNewline ? content[..^1] : content;
        var lines = body.Split('\n');
        if (!endsWithNewline)
        {
            lines[^1] += NoNewlineSentinel;
        }

        return lines;
    }

    private enum OpKind
    {
        Equal,
        Delete,
        Insert,
    }

    private readonly record struct Op(OpKind Kind, string Line);

    // Classic LCS edit script over lines (O(n·m); the files this role edits are small, and the
    // referenced-file set is bounded by the human's task statement).
    private static List<Op> Diff(string[] a, string[] b)
    {
        var n = a.Length;
        var m = b.Length;
        var lcs = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                lcs[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var ops = new List<Op>(n + m);
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (string.Equals(a[x], b[y], StringComparison.Ordinal))
            {
                ops.Add(new Op(OpKind.Equal, a[x]));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                ops.Add(new Op(OpKind.Delete, a[x]));
                x++;
            }
            else
            {
                ops.Add(new Op(OpKind.Insert, b[y]));
                y++;
            }
        }

        while (x < n)
        {
            ops.Add(new Op(OpKind.Delete, a[x++]));
        }

        while (y < m)
        {
            ops.Add(new Op(OpKind.Insert, b[y++]));
        }

        return ops;
    }

    private static IEnumerable<string> GroupIntoHunks(List<Op> ops)
    {
        // Indices into `ops` of every changed op.
        var changeIndices = new List<int>();
        for (var i = 0; i < ops.Count; i++)
        {
            if (ops[i].Kind != OpKind.Equal)
            {
                changeIndices.Add(i);
            }
        }

        if (changeIndices.Count == 0)
        {
            yield break;
        }

        // Precompute the old/new line number at which each op starts (1-based positions).
        var oldPos = new int[ops.Count + 1];
        var newPos = new int[ops.Count + 1];
        int op = 0, np = 0;
        for (var i = 0; i < ops.Count; i++)
        {
            oldPos[i] = op;
            newPos[i] = np;
            if (ops[i].Kind != OpKind.Insert)
            {
                op++;
            }

            if (ops[i].Kind != OpKind.Delete)
            {
                np++;
            }
        }

        oldPos[ops.Count] = op;
        newPos[ops.Count] = np;

        var start = Math.Max(0, changeIndices[0] - ContextLines);
        var end = Math.Min(ops.Count - 1, changeIndices[0] + ContextLines);
        for (var c = 1; c < changeIndices.Count; c++)
        {
            var nextStart = Math.Max(0, changeIndices[c] - ContextLines);
            if (nextStart <= end + 1)
            {
                end = Math.Min(ops.Count - 1, changeIndices[c] + ContextLines);
                continue;
            }

            yield return RenderHunk(ops, start, end, oldPos, newPos);
            start = nextStart;
            end = Math.Min(ops.Count - 1, changeIndices[c] + ContextLines);
        }

        yield return RenderHunk(ops, start, end, oldPos, newPos);
    }

    private static string RenderHunk(List<Op> ops, int start, int end, int[] oldPos, int[] newPos)
    {
        int oldLen = 0, newLen = 0;
        for (var i = start; i <= end; i++)
        {
            if (ops[i].Kind != OpKind.Insert)
            {
                oldLen++;
            }

            if (ops[i].Kind != OpKind.Delete)
            {
                newLen++;
            }
        }

        // git prints a zero-length range with the line *before* the range; otherwise 1-based start.
        var oldStart = oldLen == 0 ? oldPos[start] : oldPos[start] + 1;
        var newStart = newLen == 0 ? newPos[start] : newPos[start] + 1;

        var builder = new StringBuilder();
        builder.Append("@@ -").Append(oldStart).Append(',').Append(oldLen)
               .Append(" +").Append(newStart).Append(',').Append(newLen).Append(" @@\n");

        for (var i = start; i <= end; i++)
        {
            var (kind, line) = ops[i];
            var prefix = kind switch
            {
                OpKind.Equal => ' ',
                OpKind.Delete => '-',
                _ => '+',
            };

            var hasNoNewline = line.Length > 0 && line[^1] == NoNewlineSentinel;
            builder.Append(prefix).Append(hasNoNewline ? line[..^1] : line).Append('\n');
            if (hasNoNewline)
            {
                builder.Append(NoNewlineMarker).Append('\n');
            }
        }

        return builder.ToString();
    }
}
