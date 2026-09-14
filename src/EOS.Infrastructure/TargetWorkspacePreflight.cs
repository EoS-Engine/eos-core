using System.Diagnostics;
using System.Text;

namespace EOS.Infrastructure;

/// <summary>
/// ADR-010 Slice S1: validates one explicitly selected local Git Target Workspace before any
/// external engineering planning can begin. This is pre-profile/pre-manifest only: no project
/// discovery, source inspection, build/test execution, package inspection, or final
/// TargetWorkspaceContext construction happens here.
/// </summary>
public sealed class TargetWorkspacePreflight
{
    private const int TimeoutSeconds = 30;
    private const int MaxOutputCharacters = 4000;

    public async Task<TargetWorkspacePreflightResult> ValidateAsync(
        string? selectedPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            throw new TargetWorkspacePreflightException("The external target path is required.");
        }

        string canonicalSelectedPath;
        try
        {
            canonicalSelectedPath = Path.GetFullPath(selectedPath);
        }
        catch (Exception ex)
        {
            throw new TargetWorkspacePreflightException($"The external target path is invalid: {ex.Message}", ex);
        }

        if (!Directory.Exists(canonicalSelectedPath))
        {
            if (File.Exists(canonicalSelectedPath))
            {
                throw new TargetWorkspacePreflightException("The external target path must be a directory.");
            }

            throw new TargetWorkspacePreflightException("The external target path does not exist.");
        }

        canonicalSelectedPath = new DirectoryInfo(canonicalSelectedPath).FullName;

        var rootResult = await RunGitAsync(canonicalSelectedPath, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (rootResult.ExitCode != 0)
        {
            throw new TargetWorkspacePreflightException($"The external target path is not inside a valid Git working tree: {rootResult.Output}");
        }

        var rootLines = SplitLines(rootResult.Output);
        if (rootLines.Count != 1)
        {
            throw new TargetWorkspacePreflightException("Git returned an ambiguous working-tree root for the external target.");
        }

        var canonicalRoot = new DirectoryInfo(Path.GetFullPath(rootLines[0])).FullName;
        if (!IsSameOrDescendant(canonicalSelectedPath, canonicalRoot))
        {
            throw new TargetWorkspacePreflightException("The external target path resolved outside its Git working-tree root.");
        }

        var headResult = await RunGitAsync(canonicalRoot, ["rev-parse", "--verify", "HEAD^{commit}"], cancellationToken);
        if (headResult.ExitCode != 0)
        {
            throw new TargetWorkspacePreflightException($"The external target must have a resolved HEAD commit: {headResult.Output}");
        }

        var headLines = SplitLines(headResult.Output);
        if (headLines.Count != 1 || !IsShaLike(headLines[0]))
        {
            throw new TargetWorkspacePreflightException("Git returned an invalid or ambiguous HEAD commit.");
        }

        var statusResult = await RunGitAsync(canonicalRoot, ["status", "--porcelain=v1", "-z", "--untracked-files=normal"], cancellationToken);
        if (statusResult.ExitCode != 0)
        {
            throw new TargetWorkspacePreflightException($"Git status failed for the external target: {statusResult.Output}");
        }

        var dirtyReason = FindDirtyReason(statusResult.Output);
        if (dirtyReason is not null)
        {
            throw new TargetWorkspacePreflightException($"The external target must be clean before execution: {dirtyReason}");
        }

        return new TargetWorkspacePreflightResult(
            CanonicalRoot: canonicalRoot,
            ResolvedHead: headLines[0],
            CleanStateValidated: true);
    }

    private static string? FindDirtyReason(string porcelainZ)
    {
        if (string.IsNullOrEmpty(porcelainZ))
        {
            return null;
        }

        var entries = porcelainZ.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in entries)
        {
            if (entry.Length < 3)
            {
                return "Git returned malformed status output.";
            }

            var index = entry[0];
            var workTree = entry[1];
            if (index == '?' && workTree == '?')
            {
                return $"untracked file '{entry[3..]}'";
            }

            if (index != ' ')
            {
                return $"staged change '{entry[3..]}'";
            }

            if (workTree != ' ')
            {
                return $"modified tracked file '{entry[3..]}'";
            }
        }

        return null;
    }

    private static bool IsSameOrDescendant(string candidate, string root)
    {
        var normalizedRoot = EnsureTrailingSeparator(root);
        return string.Equals(candidate, root, StringComparison.Ordinal)
            || candidate.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

    private static List<string> SplitLines(string output) =>
        output.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static bool IsShaLike(string value) =>
        value.Length >= 40 && value.All(Uri.IsHexDigit);

    private static async Task<(int ExitCode, string Output)> RunGitAsync(
        string workingDirectory,
        string[] arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            throw new TargetWorkspacePreflightException($"Could not start git: {ex.Message}", ex);
        }

        using (process)
        {
            try
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(linked.Token);
                var stderrTask = process.StandardError.ReadToEndAsync(linked.Token);
                await process.WaitForExitAsync(linked.Token);
                var output = (await stdoutTask) + (await stderrTask);
                return (process.ExitCode, Bound(output.Trim()));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                TryKill(process);
                throw new TargetWorkspacePreflightException($"Git command timed out after {TimeoutSeconds} seconds.");
            }
            catch (Exception ex)
            {
                TryKill(process);
                throw new TargetWorkspacePreflightException($"Git command failed: {ex.Message}", ex);
            }
        }
    }

    private static string Bound(string text)
    {
        if (text.Length <= MaxOutputCharacters)
        {
            return text;
        }

        return text[..MaxOutputCharacters];
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
            // Best effort: the preflight has already failed closed.
        }
    }
}

public sealed record TargetWorkspacePreflightResult(
    string CanonicalRoot,
    string ResolvedHead,
    bool CleanStateValidated);

public sealed class TargetWorkspacePreflightException : InvalidOperationException
{
    public TargetWorkspacePreflightException(string message)
        : base(message)
    {
    }

    public TargetWorkspacePreflightException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
