using System.Diagnostics;
using System.Text;
using EOS.Contracts;

namespace EOS.Infrastructure;

/// <summary>
/// ADR-009: executes Constitution §0.8.1 Universal Gate 1 (static analysis — in this repository
/// <c>dotnet build</c> with <c>TreatWarningsAsErrors</c> and analyzers on) and Gate 2 (unit tests)
/// for a candidate unified diff, in an <b>isolated throwaway copy</b> of the workspace. The real
/// working tree, index and repository metadata are never read for writing, never mutated, and never
/// built in place: the copy contains only <c>src/</c>, <c>tests/</c>, <c>config/</c> and the root
/// build files (<c>Directory.Build.props</c>, <c>global.json</c>, <c>EOS.slnx</c>, <c>.editorconfig</c>) — never
/// <c>.git</c>, <c>bin</c>, or <c>.env</c>. Existing <c>obj/</c> state is copied so the build
/// runs <c>--no-restore</c>: offline, deterministic, and free of network-dependent restore/audit.
///
/// Affected projects are derived deterministically from the diff's own paths (Constitution Part 1
/// solution structure: <c>src/&lt;Project&gt;/…</c> and <c>tests/&lt;Project&gt;/…</c> are owned by
/// <c>&lt;Project&gt;.csproj</c> in that directory; a source project <c>EOS.X</c> is exercised by
/// <c>tests/EOS.X.Tests</c> when it exists). Nothing is crawled or inferred beyond that rule.
///
/// Measurement only: the pass/fail decision is the Rule Engine's (Protection §10.3). Every failure
/// to run (missing toolchain, spawn failure, timeout, unexpected output) is reported as a failed
/// step — never as passed. Cancellation kills the child process tree and propagates. The copy is
/// deleted in all cases.
/// </summary>
public sealed class IsolatedUniversalGateRunner
{
    private const int MaxDetailCharacters = 4000;
    private static readonly string[] CopiedDirectories = ["src", "tests", "config"];
    private static readonly string[] CopiedRootFiles = ["Directory.Build.props", "global.json", "EOS.slnx", ".editorconfig"];
    private static readonly string[] ExcludedDirectoryNames = ["bin", ".git"];

    private readonly string _rootDirectory;
    private readonly TimeSpan _stepTimeout;
    private readonly string _dotnetExecutable;

    /// <param name="rootDirectory">The real repository root; only ever read.</param>
    /// <param name="stepTimeout">Upper bound for each spawned step (apply, build, test); default 10 minutes.</param>
    /// <param name="dotnetExecutable">The .NET CLI to run; default <c>dotnet</c> resolved by the host's process-path rules.</param>
    public IsolatedUniversalGateRunner(string rootDirectory, TimeSpan? stepTimeout = null, string dotnetExecutable = "dotnet")
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _stepTimeout = stepTimeout ?? TimeSpan.FromMinutes(10);
        _dotnetExecutable = dotnetExecutable;
    }

    public async Task<UniversalGateResult> RunAsync(string unifiedDiff, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(unifiedDiff))
        {
            return new UniversalGateResult(new GateStepResult(GateStepStatus.Failed, "The diff is empty."), NotRun("Gate 1 failed."));
        }

        var changedPaths = ExtractChangedPaths(unifiedDiff);
        if (changedPaths.Count == 0)
        {
            return new UniversalGateResult(new GateStepResult(GateStepStatus.Failed, "The diff names no changed file."), NotRun("Gate 1 failed."));
        }

        var copyRoot = Path.Combine(Path.GetTempPath(), $"eos-gate-{Guid.NewGuid():N}");
        try
        {
            CopyWorkspace(copyRoot);

            var buildProjects = new List<string>();
            var testProjects = new List<string>();
            foreach (var path in changedPaths)
            {
                var owner = ResolveOwningProject(copyRoot, path);
                if (owner is null)
                {
                    return new UniversalGateResult(
                        new GateStepResult(GateStepStatus.Failed, $"No owning project found for changed path '{path}'."), NotRun("Gate 1 failed."));
                }

                AddDistinct(buildProjects, owner);
                if (owner.StartsWith("tests/", StringComparison.Ordinal))
                {
                    AddDistinct(testProjects, owner);
                }
                else
                {
                    var projectName = owner.Split('/')[1];
                    var derivedTests = $"tests/{projectName}.Tests/{projectName}.Tests.csproj";
                    if (File.Exists(Path.Combine(copyRoot, derivedTests)))
                    {
                        AddDistinct(buildProjects, derivedTests);
                        AddDistinct(testProjects, derivedTests);
                    }
                }
            }

            var apply = await RunProcessAsync(copyRoot, "git", ["apply", "--"], unifiedDiff, cancellationToken);
            if (apply.ExitCode != 0)
            {
                return new UniversalGateResult(
                    new GateStepResult(GateStepStatus.Failed, Bound($"git apply failed in the isolated copy: {apply.Output}")), NotRun("Gate 1 failed."));
            }

            foreach (var project in buildProjects)
            {
                var build = await RunProcessAsync(copyRoot, _dotnetExecutable, ["build", project, "--no-restore", "-nologo"], null, cancellationToken);
                if (build.ExitCode != 0)
                {
                    return new UniversalGateResult(
                        new GateStepResult(GateStepStatus.Failed, Bound($"dotnet build {project} failed: {build.Output}")), NotRun("Gate 1 failed."));
                }
            }

            var buildGate = new GateStepResult(GateStepStatus.Passed, $"Built: {string.Join(", ", buildProjects)}");

            if (testProjects.Count == 0)
            {
                return new UniversalGateResult(buildGate, new GateStepResult(GateStepStatus.NotApplicable, "No test project owns or covers the changed paths."));
            }

            foreach (var project in testProjects)
            {
                var test = await RunProcessAsync(copyRoot, _dotnetExecutable, ["test", project, "--no-build", "--no-restore", "-nologo"], null, cancellationToken);
                if (test.ExitCode != 0)
                {
                    return new UniversalGateResult(buildGate, new GateStepResult(GateStepStatus.Failed, Bound($"dotnet test {project} failed: {test.Output}")));
                }
            }

            return new UniversalGateResult(buildGate, new GateStepResult(GateStepStatus.Passed, $"Tested: {string.Join(", ", testProjects)}"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UniversalGateResult(new GateStepResult(GateStepStatus.Failed, Bound($"Gate execution failed: {ex.Message}")), NotRun("Gate 1 failed."));
        }
        finally
        {
            TryDelete(copyRoot);
        }
    }

    /// <summary>Changed paths: every <c>+++ b/</c> and <c>--- a/</c> path in the diff (deletions included), deduplicated, in order.</summary>
    public static IReadOnlyList<string> ExtractChangedPaths(string unifiedDiff)
    {
        var paths = new List<string>();
        foreach (var rawLine in unifiedDiff.Replace("\r\n", "\n").Split('\n'))
        {
            string? path = null;
            if (rawLine.StartsWith("+++ b/", StringComparison.Ordinal))
            {
                path = rawLine[6..];
            }
            else if (rawLine.StartsWith("--- a/", StringComparison.Ordinal))
            {
                path = rawLine[6..];
            }

            if (path is null)
            {
                continue;
            }

            var tab = path.IndexOf('\t');
            path = (tab < 0 ? path : path[..tab]).Trim();
            if (path.Length > 0 && !paths.Contains(path, StringComparer.Ordinal))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>
    /// Deterministic ownership rule (Constitution Part 1 §1.1): <c>src/P/…</c> → <c>src/P/P.csproj</c>,
    /// <c>tests/P/…</c> → <c>tests/P/P.csproj</c>; <see langword="null"/> when no such project file exists.
    /// </summary>
    public static string? ResolveOwningProject(string root, string changedPath)
    {
        var segments = changedPath.Split('/');
        if (segments.Length < 3 || segments[0] is not ("src" or "tests") || segments.Any(s => s.Length == 0 || s == "." || s == ".."))
        {
            return null;
        }

        var project = $"{segments[0]}/{segments[1]}/{segments[1]}.csproj";
        return File.Exists(Path.Combine(root, project)) ? project : null;
    }

    private void CopyWorkspace(string copyRoot)
    {
        Directory.CreateDirectory(copyRoot);
        foreach (var file in CopiedRootFiles)
        {
            var source = Path.Combine(_rootDirectory, file);
            if (File.Exists(source))
            {
                File.Copy(source, Path.Combine(copyRoot, file));
            }
        }

        foreach (var directory in CopiedDirectories)
        {
            var source = Path.Combine(_rootDirectory, directory);
            if (Directory.Exists(source))
            {
                CopyDirectory(source, Path.Combine(copyRoot, directory));
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (ExcludedDirectoryNames.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destination, name));
        }
    }

    private async Task<(int ExitCode, string Output)> RunProcessAsync(
        string workingDirectory, string fileName, string[] arguments, string? standardInput, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The copy has no .git; keep git from resolving any repository above it. Keep the .NET CLI
        // quiet, and keep it from leaving long-lived build servers (MSBuild nodes, the Roslyn
        // compiler server) behind: those would inherit this process's redirected pipes and outlive
        // the step, and a gate run must end when its own process ends.
        startInfo.Environment["GIT_CEILING_DIRECTORIES"] = Path.GetDirectoryName(workingDirectory) ?? workingDirectory;
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["UseSharedCompilation"] = "false";

        using var timeout = new CancellationTokenSource(_stepTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("Process.Start returned null.");
        }
        catch (Exception ex)
        {
            return (-1, $"could not start '{fileName}': {ex.Message}");
        }

        using (process)
        {
            var stdout = new BoundedTail(MaxDetailCharacters);
            var stderr = new BoundedTail(MaxDetailCharacters);
            try
            {
                var stdoutTask = stdout.DrainAsync(process.StandardOutput, linked.Token);
                var stderrTask = stderr.DrainAsync(process.StandardError, linked.Token);
                try
                {
                    if (standardInput is not null)
                    {
                        await process.StandardInput.WriteAsync(standardInput.AsMemory(), linked.Token);
                    }

                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                    // Child exited before consuming stdin; its exit code and output are the outcome.
                }

                await process.WaitForExitAsync(linked.Token);

                // Grandchildren that inherited the pipes may keep them open after the child has
                // exited; the child's exit is the step's end, so the drain is only given a grace period.
                await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), Task.Delay(TimeSpan.FromSeconds(5), linked.Token));
                return (process.ExitCode, stdout.Text + "\n" + stderr.Text);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                TryKill(process);
                return (-1, $"'{fileName} {string.Join(' ', arguments)}' timed out after {_stepTimeout.TotalSeconds:F0} seconds. {stdout.Text}\n{stderr.Text}");
            }
            catch (Exception ex)
            {
                TryKill(process);
                return (-1, $"'{fileName}' failed: {ex.Message}");
            }
        }
    }

    /// <summary>Keeps only the last <c>capacity</c> characters read from a stream — the end of a build/test log is where the verdict is.</summary>
    private sealed class BoundedTail(int capacity)
    {
        private readonly StringBuilder _buffer = new();
        private readonly Lock _lock = new();

        public string Text
        {
            get
            {
                lock (_lock)
                {
                    return _buffer.ToString();
                }
            }
        }

        public async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
        {
            var chunk = new char[4096];
            int read;
            while ((read = await reader.ReadAsync(chunk.AsMemory(), cancellationToken)) > 0)
            {
                lock (_lock)
                {
                    _buffer.Append(chunk, 0, read);
                    if (_buffer.Length > capacity)
                    {
                        _buffer.Remove(0, _buffer.Length - capacity);
                    }
                }
            }
        }
    }

    private static GateStepResult NotRun(string reason) => new(GateStepStatus.NotApplicable, $"Not run — {reason}");

    private static void AddDistinct(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.Ordinal))
        {
            list.Add(value);
        }
    }

    private static string Bound(string text) =>
        text.Length <= MaxDetailCharacters ? text : "…" + text[^MaxDetailCharacters..];

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
            // Best effort; the step has already been reported as failed/cancelled.
        }
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception)
        {
            // Best effort cleanup of the throwaway copy.
        }
    }
}
