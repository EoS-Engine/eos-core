using System.Diagnostics;
using EOS.Contracts;

namespace EOS.Infrastructure;

/// <summary>
/// Post-Roadmap WP-A: the "actual file I/O" half of Protection-Layer-Specification-v1.0 §11's
/// "Local Files" domain, performed by the project Constitution Part 1 assigns external
/// integrations to. Read-only, bound to one repository root, and limited to the Constitution
/// Part 1 §1.1 solution directories an Autonomous Role may act on in WP-A: <c>src/</c> and
/// <c>tests/</c>. Every other location — <c>config/</c>, <c>docs/</c>, <c>deploy/</c>,
/// <c>.env</c>, the solution root, anything reached by <c>..</c>, absolute or drive-letter
/// paths — is rejected before any I/O. The Protection check itself (which role may read) is
/// applied by the composition root's wrapper, not here (Protection §10: structural enforcement).
/// </summary>
public sealed class WorkspaceReader(string rootDirectory) : IWorkspaceClient
{
    private static readonly string[] PermittedRoots = ["src", "tests"];

    private readonly string _rootDirectory = Path.GetFullPath(rootDirectory);

    // ADR-007: bounds for the applicability check — the child process is killed at the timeout,
    // and captured output is truncated so a hostile or runaway diff cannot inflate the recorded
    // BlockedReason. Both are implementation constants, not configuration (no consumer reads
    // them; KISS/YAGNI).
    private const int ApplicabilityCheckTimeoutSeconds = 30;
    private const int MaxCapturedOutputCharacters = 2000;

    public Task<string?> ReadFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePermittedPath(relativePath);

        if (!File.Exists(fullPath))
        {
            return Task.FromResult<string?>(null);
        }

        return File.ReadAllTextAsync(fullPath, cancellationToken)!;
    }

    /// <summary>
    /// ADR-007: <c>git apply --check</c> against the fixed workspace root — the minimum
    /// applicability gate. Read-only by construction: <c>--check</c> alone (no <c>--index</c>,
    /// <c>--cached</c>, <c>--3way</c>, <c>--recount</c>, <c>--unsafe-paths</c>) never touches the
    /// working tree, the index, or repository metadata; <c>git apply</c> itself also refuses
    /// <c>..</c>, absolute paths, and paths through symbolic links. Security posture: no shell
    /// (<see cref="ProcessStartInfo.UseShellExecute"/> false, fixed argument list), the diff is
    /// supplied on standard input — never as an argument, never written to a file — and the
    /// working directory is the reader's root, never derived from patch content. Fails closed:
    /// a missing <c>git</c>, a spawn failure, a non-zero exit, a timeout, or any I/O/encoding
    /// failure is reported as not applicable with a bounded error; cancellation kills the child
    /// and propagates as <see cref="OperationCanceledException"/>.
    /// </summary>
    public async Task<PatchApplicabilityResult> CheckPatchAppliesAsync(string unifiedDiff, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(unifiedDiff))
        {
            return new PatchApplicabilityResult(false, "The diff is empty.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = _rootDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("apply");
        startInfo.ArgumentList.Add("--check");
        startInfo.ArgumentList.Add("--");
        // Pin git to this root: it must never resolve a repository above the workspace.
        startInfo.Environment["GIT_CEILING_DIRECTORIES"] = Path.GetDirectoryName(_rootDirectory) ?? _rootDirectory;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(ApplicabilityCheckTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            return new PatchApplicabilityResult(false, Bound($"The applicability check could not start git: {ex.Message}"));
        }

        using (process)
        {
            try
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);

                try
                {
                    await process.StandardInput.WriteAsync(unifiedDiff.AsMemory(), linked.Token);
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // The child exited before consuming its input (broken pipe). Its exit code
                    // and stderr — not the write failure — are the informative outcome, so fall
                    // through to WaitForExit and report those; the result is still fail-closed.
                }

                await process.WaitForExitAsync(linked.Token);
                var stdout = await stdoutTask;
                var stderr = await stderrTask;

                if (process.ExitCode != 0)
                {
                    var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    return new PatchApplicabilityResult(false, Bound($"git apply --check exited with code {process.ExitCode}: {detail.Trim()}"));
                }

                return new PatchApplicabilityResult(true, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                TryKill(process);
                return new PatchApplicabilityResult(false, $"The applicability check timed out after {ApplicabilityCheckTimeoutSeconds} seconds.");
            }
            catch (Exception ex)
            {
                TryKill(process);
                return new PatchApplicabilityResult(false, Bound($"The applicability check failed: {ex.Message}"));
            }
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // Best effort only — the check has already been reported as failed/cancelled.
        }
    }

    private static string Bound(string text) =>
        text.Length <= MaxCapturedOutputCharacters ? text : text[..MaxCapturedOutputCharacters] + "…";

    /// <summary>
    /// Deterministic path rule shared by the reader and its tests: repository-root-relative,
    /// forward slashes, no absolute/drive-letter/backslash form, no <c>.</c>/<c>..</c>/empty
    /// segment, first segment exactly <c>src</c> or <c>tests</c>, at least one further segment.
    /// The resolved full path is additionally required to stay under the root — defence in depth
    /// against any platform-specific path form the segment rules did not anticipate.
    /// </summary>
    public static bool IsPermittedRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || relativePath.Contains('\\')
            || relativePath.Contains('\0')
            || relativePath.StartsWith('/')
            || (relativePath.Length >= 2 && char.IsLetter(relativePath[0]) && relativePath[1] == ':'))
        {
            return false;
        }

        var segments = relativePath.Split('/');
        if (segments.Length < 2 || !PermittedRoots.Contains(segments[0], StringComparer.Ordinal))
        {
            return false;
        }

        return segments.All(segment => segment.Length > 0 && segment != "." && segment != "..");
    }

    private string ResolvePermittedPath(string relativePath)
    {
        if (!IsPermittedRelativePath(relativePath))
        {
            throw new ArgumentException(
                $"'{relativePath}' is not a permitted repository path: only root-relative paths under src/ or tests/ may be read.",
                nameof(relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, relativePath));
        var rootWithSeparator = _rootDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? _rootDirectory
            : _rootDirectory + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{relativePath}' resolves outside the repository root.", nameof(relativePath));
        }

        return fullPath;
    }
}
