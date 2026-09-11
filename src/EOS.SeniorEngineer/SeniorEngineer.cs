using System.Text;
using System.Text.RegularExpressions;
using EOS.Contracts;

namespace EOS.SeniorEngineer;

/// <summary>
/// Post-Roadmap WP-A: the first Autonomous Role with behaviour — Constitution §0.2.1's Senior
/// Engineer (L1: "Implementation, mentoring, task execution"), realizing Part 6 §6.2's "Role
/// executes" for one dispatched Task. It executes a human-scoped engineering task by reading only
/// the explicitly referenced repository files through <see cref="IWorkspaceClient"/> (Protection-
/// gated by the composition root, Protection §11 "Local Files"), asking the Reasoning Engine —
/// exactly one <see cref="IReasoningEngineClient.ReasonAsync"/> call, never <c>IAIProviderClient</c>
/// (Constitution Part 1 §1.2: <c>EOS.Reasoning</c> is its sole consumer) — for exact edit blocks
/// (ADR-008: the model never writes a diff; it copies existing lines verbatim and supplies their
/// replacement), applying those blocks to in-memory copies only, deriving the unified diff
/// deterministically (<see cref="UnifiedDiffBuilder"/>), validating that diff structurally and
/// for applicability to the workspace (ADR-007), and registering it as immutable Evidence
/// (Part 6 §6.2: "Implementation evidence (diff, artifact)"). It never writes files, never applies
/// the diff, never claims the change compiled or was tested; a human reviews, applies, tests,
/// commits and merges. Any inability to produce a valid diff is thrown, never masked — the
/// Execution Coordinator records the Constitution's <c>Running → Blocked</c> transition.
///
/// Depends on <c>EOS.Contracts</c> only (Constitution Part 1 §1.2, Part 11 §11.2; fitness rule
/// R-02 enforced by <c>EOS.ArchitectureTests</c>). The path rule below intentionally mirrors
/// <c>EOS.Infrastructure.WorkspaceReader</c>'s (which this project may not reference) so that the
/// diff validator and the reader agree on what a permitted repository path is.
/// </summary>
public sealed class SeniorEngineer(
    IReasoningEngineClient reasoningEngineClient,
    IWorkspaceClient workspaceClient,
    IArtifactRegistryClient artifactRegistryClient) : ITaskExecutionClient
{
    public const string RoleName = "SeniorEngineer";
    public const string ProducerName = "EOS.SeniorEngineer";
    public const string EvidenceArtifactType = "Evidence";

    private const string MissingFileMarker = "(does not exist — create it)";

    // Deterministic, inference-free scoping: a referenced path is any token rooted at src/ or
    // tests/ that is not glued to a preceding path character. Trailing sentence punctuation is
    // trimmed. No other path source exists — no crawling, no listing, no inference.
    private static readonly Regex ReferencedPathPattern = new(
        @"(?<![A-Za-z0-9_./-])(?:src|tests)/[A-Za-z0-9_./-]+", RegexOptions.Compiled);

    private static readonly string[] PermittedRoots = ["src", "tests"];

    public async Task<TaskExecutionResult> ExecuteAsync(DispatchedTask task, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        var referencedPaths = ExtractReferencedPaths(task.Description);
        if (referencedPaths.Count == 0)
        {
            throw new InvalidOperationException(
                "The task references no repository path under src/ or tests/; the Senior Engineer cannot act on an unscoped task.");
        }

        var files = new List<(string Path, string? Content)>();
        foreach (var path in referencedPaths)
        {
            files.Add((path, await workspaceClient.ReadFileAsync(path, cancellationToken)));
        }

        var request = new ReasoningRequest(
            RequestId: Guid.NewGuid(),
            CorrelationId: task.TaskId,
            Goal: BuildGoal(task.Description, files),
            RequestingRole: RoleName,
            ReasoningType: ReasoningType.EngineeringReasoning,
            Constraints:
            [
                "You are NOT writing a diff or a patch. Respond ONLY with one or more edit blocks in exactly this form, and nothing else:",
                "[EDIT]\nFILE: <one of the referenced files, exactly as listed>\nSEARCH:\n<1 to 6 consecutive lines copied character-for-character from that file, including leading spaces>\nREPLACE:\n<those same lines with the requested change applied>\n[/EDIT]",
                "SEARCH must occur exactly once in the file and must be copied verbatim — never retyped, re-indented, abbreviated, or invented.",
                "To insert new code, put in SEARCH the 1-3 existing lines directly before the insertion point and repeat them in REPLACE followed by the new lines.",
                "For a referenced file marked '" + MissingFileMarker + "', leave SEARCH empty and put the complete new file content in REPLACE.",
                "Edit only the referenced files. No explanations, no markdown, no line numbers, no hunk headers, no 'diff --git', no '---'/'+++', no '@@'.",
            ]);

        var decisions = await reasoningEngineClient.ReasonAsync(request, cancellationToken);
        var editBlocks = ExtractEditBlocks(decisions[0].SelectedHypothesis);

        // ADR-008: the model's blocks are applied to the in-memory copies read above (never to
        // the workspace) under strict rules — referenced file only; verbatim, line-aligned,
        // unique SEARCH for an existing file; empty SEARCH only for a file the reader reported as
        // missing — and the unified diff is then a deterministic function of (original, modified):
        // hunk offsets, counts, markers and paths are never under model control.
        var modifiedFiles = ApplyEditBlocks(editBlocks, files);
        var diff = BuildUnifiedDiff(files, modifiedFiles);

        var violation = ValidateUnifiedDiff(diff, referencedPaths);
        if (violation is not null)
        {
            throw new InvalidOperationException($"The produced diff is not a valid, in-scope unified diff: {violation}");
        }

        // ADR-007: structural validity is not applicability. Only a diff that applies, as-is, to
        // the workspace it was generated from is implementation evidence (Constitution Part 6
        // §6.2, §0.1.1.6) — checked after the structural/security rules above (so nothing
        // out-of-scope ever reaches the checker) and before registration (so a non-applicable
        // diff is never registered). Applicability says nothing about intent, compilation, or
        // tests; those remain human Review/Testing.
        var applicability = await workspaceClient.CheckPatchAppliesAsync(diff, cancellationToken);
        if (!applicability.Applies)
        {
            throw new InvalidOperationException($"The produced diff does not apply to the workspace: {applicability.Error}");
        }

        var artifact = await artifactRegistryClient.RegisterAsync(
            EvidenceArtifactType, ProducerName, diff, previousVersionHash: null, cancellationToken);

        return new TaskExecutionResult([$"artifact:{artifact.ContentHash}"]);
    }

    /// <summary>One model edit: replace <see cref="Search"/> in <see cref="Path"/> with <see cref="Replace"/> (ADR-008).</summary>
    public sealed record EditBlock(string Path, string Search, string Replace);

    private const string EditOpen = "[EDIT]";
    private const string EditClose = "[/EDIT]";
    private const string FilePrefix = "FILE:";
    private const string SearchLabel = "SEARCH:";
    private const string ReplaceLabel = "REPLACE:";

    /// <summary>
    /// Parses every <c>[EDIT] … [/EDIT]</c> block, in order. Text outside blocks is ignored (the
    /// blocks, not the prose, are what gets validated); a block that is unterminated or lacks
    /// <c>FILE:</c>/<c>SEARCH:</c>/<c>REPLACE:</c> in that order is malformed and throws, as does
    /// output containing no block at all. SEARCH/REPLACE bodies are the exact lines between the
    /// labels (no trailing newline is implied).
    /// </summary>
    public static IReadOnlyList<EditBlock> ExtractEditBlocks(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("The reasoning output contains no [EDIT] block.");
        }

        var normalized = text.Replace("\r\n", "\n");
        var blocks = new List<EditBlock>();
        var position = 0;
        while (true)
        {
            var open = normalized.IndexOf(EditOpen, position, StringComparison.Ordinal);
            if (open < 0)
            {
                break;
            }

            var close = normalized.IndexOf(EditClose, open + EditOpen.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                throw new InvalidOperationException("Malformed edit block: [EDIT] without a matching [/EDIT].");
            }

            var body = normalized[(open + EditOpen.Length)..close];
            blocks.Add(ParseEditBlock(body));
            position = close + EditClose.Length;
        }

        if (blocks.Count == 0)
        {
            throw new InvalidOperationException("The reasoning output contains no [EDIT] block.");
        }

        return blocks;
    }

    private static EditBlock ParseEditBlock(string body)
    {
        var lines = body.Split('\n').ToList();
        // Tolerate the newline that normally follows "[EDIT]" and precedes "[/EDIT]".
        if (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count == 0 || !lines[0].StartsWith(FilePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Malformed edit block: the first line must be 'FILE: <path>'.");
        }

        var path = lines[0][FilePrefix.Length..].Trim();
        if (lines.Count < 2 || lines[1] != SearchLabel)
        {
            throw new InvalidOperationException("Malformed edit block: 'FILE:' must be followed by a 'SEARCH:' line.");
        }

        var replaceIndex = lines.IndexOf(ReplaceLabel, 2);
        if (replaceIndex < 0)
        {
            throw new InvalidOperationException("Malformed edit block: no 'REPLACE:' line after 'SEARCH:'.");
        }

        var search = string.Join('\n', lines.Skip(2).Take(replaceIndex - 2));
        var replace = string.Join('\n', lines.Skip(replaceIndex + 1));
        return new EditBlock(path, search, replace);
    }

    /// <summary>
    /// Applies <paramref name="blocks"/> in order to in-memory copies of <paramref name="files"/>
    /// (path → current content, <see langword="null"/> = reported missing) and returns the
    /// resulting contents. Fails closed on: a path that is not a referenced file; an empty SEARCH
    /// for an existing file; a non-empty SEARCH for a missing file; a SEARCH that does not occur
    /// exactly once (character-for-character, whitespace significant, aligned to whole lines) in
    /// the file's <em>current</em> in-memory content. No fuzzy matching, no normalization, no
    /// re-indentation. The workspace is never touched.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ApplyEditBlocks(
        IReadOnlyList<EditBlock> blocks, IReadOnlyList<(string Path, string? Content)> files)
    {
        var contents = files.ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);

        foreach (var block in blocks)
        {
            if (!IsPermittedRelativePath(block.Path) || !contents.TryGetValue(block.Path, out var current))
            {
                throw new InvalidOperationException($"Edit block targets '{block.Path}', which is not one of the referenced files.");
            }

            if (current is null)
            {
                if (block.Search.Length != 0)
                {
                    throw new InvalidOperationException(
                        $"Edit block for '{block.Path}' has a non-empty SEARCH, but the file does not exist; creation requires an empty SEARCH.");
                }

                if (block.Replace.Length == 0)
                {
                    throw new InvalidOperationException($"Edit block creating '{block.Path}' has an empty REPLACE.");
                }

                contents[block.Path] = block.Replace.EndsWith('\n') ? block.Replace : block.Replace + "\n";
                continue;
            }

            if (block.Search.Length == 0)
            {
                throw new InvalidOperationException($"Edit block for existing file '{block.Path}' has an empty SEARCH.");
            }

            var occurrences = CountLineAlignedOccurrences(current, block.Search);
            if (occurrences == 0)
            {
                throw new InvalidOperationException(
                    $"SEARCH text for '{block.Path}' was not found verbatim (whitespace-exact, whole lines) in the file.");
            }

            if (occurrences > 1)
            {
                throw new InvalidOperationException($"SEARCH text for '{block.Path}' occurs {occurrences} times; it must be unique.");
            }

            var index = FindLineAlignedOccurrence(current, block.Search, 0);
            contents[block.Path] = string.Concat(current.AsSpan(0, index), block.Replace, current.AsSpan(index + block.Search.Length));
        }

        return contents;
    }

    private static string BuildUnifiedDiff(
        IReadOnlyList<(string Path, string? Content)> files, IReadOnlyDictionary<string, string?> modifiedFiles)
    {
        var builder = new StringBuilder();
        foreach (var (path, original) in files)
        {
            var modified = modifiedFiles[path];
            if (original is null && modified is null)
            {
                continue;
            }

            var fileDiff = UnifiedDiffBuilder.Build(path, original, modified);
            if (fileDiff is not null)
            {
                builder.Append(fileDiff);
            }
        }

        if (builder.Length == 0)
        {
            throw new InvalidOperationException("The edit blocks produced no change to any referenced file.");
        }

        return builder.ToString();
    }

    // A SEARCH occurrence counts only when it starts at the beginning of a line and ends at the
    // end of a line, so a block can never match part-way through a line.
    private static int CountLineAlignedOccurrences(string content, string search)
    {
        var count = 0;
        var index = FindLineAlignedOccurrence(content, search, 0);
        while (index >= 0)
        {
            count++;
            index = FindLineAlignedOccurrence(content, search, index + 1);
        }

        return count;
    }

    private static int FindLineAlignedOccurrence(string content, string search, int startAt)
    {
        var index = content.IndexOf(search, startAt, StringComparison.Ordinal);
        while (index >= 0)
        {
            var startsLine = index == 0 || content[index - 1] == '\n';
            var end = index + search.Length;
            var endsLine = end == content.Length || content[end] == '\n';
            if (startsLine && endsLine)
            {
                return index;
            }

            index = content.IndexOf(search, index + 1, StringComparison.Ordinal);
        }

        return -1;
    }

    /// <summary>
    /// The human-referenced path set: every distinct, permitted <c>src/…</c> or <c>tests/…</c>
    /// token in <paramref name="description"/>, in order of first appearance.
    /// </summary>
    public static IReadOnlyList<string> ExtractReferencedPaths(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return [];
        }

        var paths = new List<string>();
        foreach (Match match in ReferencedPathPattern.Matches(description))
        {
            var candidate = match.Value.TrimEnd('.');
            if (IsPermittedRelativePath(candidate) && !paths.Contains(candidate, StringComparer.Ordinal))
            {
                paths.Add(candidate);
            }
        }

        return paths;
    }

    /// <summary>
    /// Deterministic, minimal unified-diff validation (no patch-parser framework). Returns
    /// <see langword="null"/> when valid, otherwise the first rule violated. Every path on BOTH
    /// sides of <c>diff --git</c>, <c>---</c>/<c>+++</c>, and <c>rename</c>/<c>copy</c> lines must be
    /// a permitted repository path (relative, no absolute/drive-letter/backslash form, no
    /// <c>.</c>/<c>..</c>/empty segment, rooted at <c>src</c> or <c>tests</c>) AND a member of
    /// <paramref name="referencedPaths"/>. <c>/dev/null</c> is permitted only as the <c>---</c>
    /// side (creation) or the <c>+++</c> side (deletion), never both, never elsewhere.
    /// Structural minimums: every <c>---</c> immediately followed by <c>+++</c>; at least one such
    /// pair; at least one <c>@@</c> hunk after every pair.
    /// </summary>
    public static string? ValidateUnifiedDiff(string diff, IReadOnlyList<string> referencedPaths)
    {
        if (string.IsNullOrWhiteSpace(diff))
        {
            return "the diff is empty";
        }

        var lines = diff.Replace("\r\n", "\n").Split('\n');
        var pairCount = 0;
        var hunksSincePair = -1; // -1: no pair seen yet

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                if (hunksSincePair == 0)
                {
                    return "a ---/+++ pair has no @@ hunk";
                }

                var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length != 4 || !tokens[2].StartsWith("a/", StringComparison.Ordinal) || !tokens[3].StartsWith("b/", StringComparison.Ordinal))
                {
                    return $"malformed header '{line}'";
                }

                if (CheckScopedPath(tokens[2][2..], referencedPaths) is { } oldViolation)
                {
                    return $"diff --git old path: {oldViolation}";
                }

                if (CheckScopedPath(tokens[3][2..], referencedPaths) is { } newViolation)
                {
                    return $"diff --git new path: {newViolation}";
                }

                continue;
            }

            if (line.StartsWith("rename from ", StringComparison.Ordinal) || line.StartsWith("copy from ", StringComparison.Ordinal)
                || line.StartsWith("rename to ", StringComparison.Ordinal) || line.StartsWith("copy to ", StringComparison.Ordinal))
            {
                var path = line[(line.IndexOf(' ', line.IndexOf(' ') + 1) + 1)..];
                if (CheckScopedPath(path, referencedPaths) is { } violation)
                {
                    return $"'{line}': {violation}";
                }

                continue;
            }

            if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                if (hunksSincePair == 0)
                {
                    return "a ---/+++ pair has no @@ hunk";
                }

                if (i + 1 >= lines.Length || !lines[i + 1].StartsWith("+++ ", StringComparison.Ordinal))
                {
                    return "a --- line is not immediately followed by a +++ line";
                }

                var oldPath = StripTimestamp(line[4..]);
                var newPath = StripTimestamp(lines[i + 1][4..]);
                var oldIsNull = oldPath == "/dev/null";
                var newIsNull = newPath == "/dev/null";

                if (oldIsNull && newIsNull)
                {
                    return "both sides of a file header are /dev/null";
                }

                if (!oldIsNull)
                {
                    if (!oldPath.StartsWith("a/", StringComparison.Ordinal))
                    {
                        return $"old path '{oldPath}' lacks the a/ prefix";
                    }

                    if (CheckScopedPath(oldPath[2..], referencedPaths) is { } oldViolation)
                    {
                        return $"old path: {oldViolation}";
                    }
                }

                if (!newIsNull)
                {
                    if (!newPath.StartsWith("b/", StringComparison.Ordinal))
                    {
                        return $"new path '{newPath}' lacks the b/ prefix";
                    }

                    if (CheckScopedPath(newPath[2..], referencedPaths) is { } newViolation)
                    {
                        return $"new path: {newViolation}";
                    }
                }

                pairCount++;
                hunksSincePair = 0;
                i++; // the +++ line has been consumed
                continue;
            }

            if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                return "a +++ line appears without a preceding --- line";
            }

            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                if (hunksSincePair < 0)
                {
                    return "an @@ hunk appears before any ---/+++ pair";
                }

                hunksSincePair++;
            }
        }

        if (pairCount == 0)
        {
            return "no ---/+++ file header pair";
        }

        if (hunksSincePair == 0)
        {
            return "a ---/+++ pair has no @@ hunk";
        }

        return null;
    }

    /// <summary>
    /// Extracts the body of the first <c>```diff</c> fenced block, or <see langword="null"/>. No
    /// longer on the execution path (ADR-008: the model emits edit blocks, not diffs); retained
    /// because existing tests cover it and removal is outside ADR-008's authorized change set.
    /// </summary>
    public static string? ExtractDiffFence(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var normalized = text.Replace("\r\n", "\n");
        var open = normalized.IndexOf("```diff", StringComparison.Ordinal);
        if (open < 0)
        {
            return null;
        }

        var bodyStart = normalized.IndexOf('\n', open);
        if (bodyStart < 0)
        {
            return null;
        }

        var close = normalized.IndexOf("\n```", bodyStart, StringComparison.Ordinal);
        if (close < 0)
        {
            return null;
        }

        var body = normalized[(bodyStart + 1)..close];
        return body.EndsWith('\n') ? body : body + "\n";
    }

    public static bool IsPermittedRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || path.Contains('\\')
            || path.Contains('\0')
            || path.StartsWith('/')
            || (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':'))
        {
            return false;
        }

        var segments = path.Split('/');
        if (segments.Length < 2 || !PermittedRoots.Contains(segments[0], StringComparer.Ordinal))
        {
            return false;
        }

        return segments.All(segment => segment.Length > 0 && segment != "." && segment != "..");
    }

    private static string? CheckScopedPath(string path, IReadOnlyList<string> referencedPaths)
    {
        if (!IsPermittedRelativePath(path))
        {
            return $"'{path}' is not a permitted repository path (relative, under src/ or tests/, no traversal)";
        }

        if (!referencedPaths.Contains(path, StringComparer.Ordinal))
        {
            return $"'{path}' was not referenced by the task";
        }

        return null;
    }

    private static string StripTimestamp(string path)
    {
        var tab = path.IndexOf('\t');
        return tab < 0 ? path.Trim() : path[..tab].Trim();
    }

    private static string BuildGoal(string description, IReadOnlyList<(string Path, string? Content)> files)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Implement the following engineering task and respond with exact edit blocks (see constraints).");
        builder.AppendLine();
        builder.AppendLine("Task:");
        builder.AppendLine(description);
        builder.AppendLine();
        builder.AppendLine("Referenced files:");
        foreach (var (path, content) in files)
        {
            builder.AppendLine();
            builder.AppendLine($"=== {path} ===");
            builder.AppendLine(content ?? MissingFileMarker);
        }

        return builder.ToString();
    }
}
