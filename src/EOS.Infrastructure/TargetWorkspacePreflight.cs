using System.Diagnostics;
using System.Runtime.InteropServices;
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

        var selectedPathCandidate = NormalizeSelectedPath(selectedPath);
        if (!Directory.Exists(selectedPathCandidate))
        {
            if (File.Exists(selectedPathCandidate))
            {
                throw new TargetWorkspacePreflightException("The external target path must be a directory.");
            }

            throw new TargetWorkspacePreflightException("The external target path does not exist.");
        }

        var canonicalSelectedPath = ResolvePhysicalDirectory(selectedPathCandidate);

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

        var canonicalRoot = ResolvePhysicalDirectory(rootLines[0]);
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

    private static string NormalizeSelectedPath(string selectedPath)
    {
        try
        {
            return Path.GetFullPath(selectedPath);
        }
        catch (Exception ex)
        {
            throw new TargetWorkspacePreflightException($"The external target path is invalid: {ex.Message}", ex);
        }
    }

    private static string ResolvePhysicalDirectory(string path)
    {
        var fullPath = NormalizeSelectedPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new TargetWorkspacePreflightException("The external target path does not exist.");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new DirectoryInfo(fullPath).FullName;
        }

        var result = RunRealpath(fullPath);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
        {
            throw new TargetWorkspacePreflightException($"The external target path could not be physically resolved: {result.Output}");
        }

        var lines = SplitLines(result.Output);
        if (lines.Count != 1 || !Directory.Exists(lines[0]))
        {
            throw new TargetWorkspacePreflightException("The external target path resolved to an invalid physical directory.");
        }

        return new DirectoryInfo(lines[0]).FullName;
    }

    private static (int ExitCode, string Output) RunRealpath(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "realpath",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(path);

        using var process = Process.Start(startInfo)
            ?? throw new TargetWorkspacePreflightException("Could not start realpath.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, Bound((stdout + stderr).Trim()));
    }

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
        ScrubGitRepositoryEnvironment(startInfo);
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

    private static void ScrubGitRepositoryEnvironment(ProcessStartInfo startInfo)
    {
        foreach (var variable in RepositorySelectionEnvironmentVariables)
        {
            startInfo.Environment.Remove(variable);
        }

        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
    }

    private static readonly string[] RepositorySelectionEnvironmentVariables =
    [
        "GIT_DIR",
        "GIT_WORK_TREE",
        "GIT_INDEX_FILE",
        "GIT_COMMON_DIR",
        "GIT_OBJECT_DIRECTORY",
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_PREFIX",
    ];
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
