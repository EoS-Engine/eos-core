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
/// <c>tests/EOS.X.Tests</c> when it exists). Gate 1 builds the reverse dependency closure of the
/// affected projects — every solution project that transitively references one of them, read from
/// <c>EOS.slnx</c> and the projects' own <c>&lt;ProjectReference&gt;</c> items — so a change that
/// compiles in its own project but breaks a consumer cannot pass. Nothing is crawled or inferred
/// beyond those two rules.
///
/// Gate 2 passes only when the test process exits 0 <em>and</em> its TRX result file records at
/// least one executed test; a run that executes nothing is a failed gate, not a pass.
///
/// The gate executes artifact-controlled source, test and MSBuild code. Its child processes
/// therefore receive an explicitly constructed environment (an allowlist of toolchain, locale and
/// temp variables) and never the EOS runtime's <c>EOS_*</c> connection strings or endpoints: a
/// test that needs live EOS infrastructure fails closed inside the gate instead of touching it.
/// No OS-level sandbox is applied (ADR-009 records the remaining capability).
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
    private const string SolutionFileName = "EOS.slnx";
    private const string TestResultsDirectoryName = ".eos-gate-results";
    private const string TestResultsFileName = "gate.trx";

    // H-1: the only parent variables a gate child may see. Everything else — in particular every
    // EOS_* runtime variable — is dropped; the fixed gate variables are added afterwards.
    private static readonly string[] InheritedEnvironmentNames =
        ["PATH", "HOME", "USER", "LOGNAME", "SHELL", "TMPDIR", "TMP", "TEMP", "LANG", "LC_ALL", "LC_CTYPE", "TERM"];
    private static readonly string[] InheritedEnvironmentPrefixes = ["DOTNET_", "NUGET_"];
    private const string BlockedEnvironmentPrefix = "EOS_";

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

            var affectedProjects = new List<string>();
            var testProjects = new List<string>();
            foreach (var path in changedPaths)
            {
                var owner = ResolveOwningProject(copyRoot, path);
                if (owner is null)
                {
                    return new UniversalGateResult(
                        new GateStepResult(GateStepStatus.Failed, $"No owning project found for changed path '{path}'."), NotRun("Gate 1 failed."));
                }

                AddDistinct(affectedProjects, owner);
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
                        AddDistinct(affectedProjects, derivedTests);
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

            // H-2: the build scope is the reverse dependency closure of the affected projects,
            // computed from the (patched) copy's solution and project files — so a consumer broken
            // by the change is compiled. Building the closure's roots compiles every member.
            var closure = ComputeDependencyClosure(copyRoot, affectedProjects);
            foreach (var project in closure.Roots)
            {
                var build = await RunProcessAsync(copyRoot, _dotnetExecutable, ["build", project, "--no-restore", "-nologo"], null, cancellationToken);
                if (build.ExitCode != 0)
                {
                    return new UniversalGateResult(
                        new GateStepResult(GateStepStatus.Failed, Bound($"dotnet build {project} failed: {build.Output}")), NotRun("Gate 1 failed."));
                }
            }

            var buildGate = new GateStepResult(GateStepStatus.Passed, $"Built: {string.Join(", ", closure.Projects)}");

            if (testProjects.Count == 0)
            {
                return new UniversalGateResult(buildGate, new GateStepResult(GateStepStatus.NotApplicable, "No test project owns or covers the changed paths."));
            }

            var executedTests = 0;
            for (var i = 0; i < testProjects.Count; i++)
            {
                var project = testProjects[i];
                var resultsDirectory = Path.Combine(copyRoot, TestResultsDirectoryName, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var test = await RunProcessAsync(
                    copyRoot, _dotnetExecutable,
                    ["test", project, "--no-build", "--no-restore", "-nologo", "--logger", $"trx;LogFileName={TestResultsFileName}", "--results-directory", resultsDirectory],
                    null, cancellationToken);
                if (test.ExitCode != 0)
                {
                    return new UniversalGateResult(buildGate, new GateStepResult(GateStepStatus.Failed, Bound($"dotnet test {project} failed: {test.Output}")));
                }

                // M-1: exit code 0 is not a pass on its own — the runner's TRX must record executed tests.
                var executed = ReadExecutedTestCount(Path.Combine(resultsDirectory, TestResultsFileName));
                if (executed is null or 0)
                {
                    return new UniversalGateResult(buildGate, new GateStepResult(
                        GateStepStatus.Failed,
                        Bound($"dotnet test {project} exited 0 but executed {(executed is null ? "no recorded" : "zero")} tests: {test.Output}")));
                }

                executedTests += executed.Value;
            }

            return new UniversalGateResult(buildGate, new GateStepResult(GateStepStatus.Passed, $"Tested ({executedTests} executed): {string.Join(", ", testProjects)}"));
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

        // H-1: the child sees an explicitly constructed environment, never the EOS runtime's.
        startInfo.Environment.Clear();
        foreach (var (name, value) in BuildChildEnvironment(Environment.GetEnvironmentVariables()))
        {
            startInfo.Environment[name] = value;
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

    /// <summary>
    /// H-1: the environment a gate child receives — the allowlisted toolchain/locale/temp variables
    /// of <paramref name="parent"/> only. Any <c>EOS_*</c> variable is dropped unconditionally.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildChildEnvironment(System.Collections.IDictionary parent)
    {
        var child = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in parent)
        {
            if (entry.Key is not string name || entry.Value is not string value)
            {
                continue;
            }

            if (name.StartsWith(BlockedEnvironmentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (InheritedEnvironmentNames.Contains(name, StringComparer.Ordinal)
                || InheritedEnvironmentPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                child[name] = value;
            }
        }

        return child;
    }

    /// <summary>The build scope for a set of affected projects (H-2).</summary>
    /// <param name="Projects">Every solution project in the reverse dependency closure (the affected projects and all their transitive consumers), in solution order.</param>
    /// <param name="Roots">The closure members no other closure member references; building them compiles the whole closure.</param>
    public sealed record DependencyClosure(IReadOnlyList<string> Projects, IReadOnlyList<string> Roots);

    /// <summary>
    /// H-2: reads <c>EOS.slnx</c> and every listed project's <c>&lt;ProjectReference&gt;</c> items under
    /// <paramref name="root"/> and returns the reverse dependency closure of
    /// <paramref name="affectedProjects"/> (root-relative, forward-slash paths). A project not listed
    /// in the solution is still built as itself. Deterministic: no restore, no MSBuild evaluation.
    /// </summary>
    public static DependencyClosure ComputeDependencyClosure(string root, IReadOnlyList<string> affectedProjects)
    {
        var solutionProjects = ReadSolutionProjects(root);
        var references = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var project in solutionProjects)
        {
            references[project] = ReadProjectReferences(root, project);
        }

        var closure = new List<string>();
        var pending = new Queue<string>(affectedProjects);
        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (closure.Contains(current, StringComparer.Ordinal))
            {
                continue;
            }

            closure.Add(current);
            foreach (var (project, referenced) in references)
            {
                if (referenced.Contains(current, StringComparer.Ordinal))
                {
                    pending.Enqueue(project);
                }
            }
        }

        var ordered = solutionProjects.Where(closure.Contains).Concat(closure.Where(p => !solutionProjects.Contains(p, StringComparer.Ordinal))).ToList();
        var roots = ordered
            .Where(candidate => !ordered.Any(other => references.TryGetValue(other, out var refs) && refs.Contains(candidate, StringComparer.Ordinal)))
            .ToList();
        return new DependencyClosure(ordered, roots);
    }

    private static List<string> ReadSolutionProjects(string root)
    {
        var solutionPath = Path.Combine(root, SolutionFileName);
        if (!File.Exists(solutionPath))
        {
            return [];
        }

        var document = System.Xml.Linq.XDocument.Load(solutionPath);
        return document.Descendants("Project")
            .Select(p => (string?)p.Attribute("Path"))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Replace('\\', '/'))
            .ToList();
    }

    private static List<string> ReadProjectReferences(string root, string project)
    {
        var projectPath = Path.Combine(root, project);
        if (!File.Exists(projectPath))
        {
            return [];
        }

        var projectDirectory = Path.GetDirectoryName(projectPath) ?? root;
        var document = System.Xml.Linq.XDocument.Load(projectPath);
        var result = new List<string>();
        foreach (var include in document.Descendants("ProjectReference").Select(r => (string?)r.Attribute("Include")))
        {
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            var full = Path.GetFullPath(Path.Combine(projectDirectory, include.Replace('\\', '/')));
            var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
            AddDistinct(result, relative);
        }

        return result;
    }

    /// <summary>
    /// M-1: the number of executed tests recorded by the VSTest TRX at <paramref name="trxPath"/>
    /// (<c>ResultSummary/Counters@executed</c>), or <see langword="null"/> when the file is absent or
    /// unreadable — which the caller treats as a failed gate.
    /// </summary>
    public static int? ReadExecutedTestCount(string trxPath)
    {
        try
        {
            if (!File.Exists(trxPath))
            {
                return null;
            }

            var document = System.Xml.Linq.XDocument.Load(trxPath);
            var counters = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Counters");
            var executed = (string?)counters?.Attribute("executed");
            return int.TryParse(executed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count) ? count : null;
        }
        catch (Exception)
        {
            return null;
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
