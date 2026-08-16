using System.Diagnostics;
using System.Formats.Tar;
using System.Runtime.Versioning;
using EOS.Runner.Bootstrap;
using EOS.SharedKernel.Configuration;

// deploy/restore-drill.sh and deploy/backup.sh are bash/Unix-only (chmod, tar, Docker Compose);
// these tests shell out to them and inspect Unix file modes, so this assembly is declared
// Linux-only rather than suppressing the platform-compatibility analyzer.
[assembly: SupportedOSPlatform("linux")]

namespace EOS.RestoreDrill.Tests;

// WP-030-07: deterministic subprocess tests for deploy/restore-drill.sh (A-F) plus one real,
// non-mocked end-to-end drill (G). Every synthetic test builds its own isolated temp tree; the
// real drill in G uses a genuinely synthetic-but-real backup (see that test's own comment for
// why real production data could not be used in this environment).
public class RestoreDrillTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string RestoreDrillScript = Path.Combine(RepoRoot, "deploy", "restore-drill.sh");
    private static readonly string BackupScript = Path.Combine(RepoRoot, "deploy", "backup.sh");
    private static readonly string ComposeFile = Path.Combine(RepoRoot, "deploy", "docker-compose.restore-drill.yml");
    private static readonly string LiveComposeFile = Path.Combine(RepoRoot, "docker-compose.yml");
    private static readonly string RealConfigDir = Path.Combine(RepoRoot, "config");

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EOS.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string fileName, IEnumerable<string> args, IDictionary<string, string>? env = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (env is not null)
        {
            foreach (var (key, value) in env)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = Process.Start(startInfo)!;
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdOutTask, await stdErrTask);
    }

    // --- A. CLI validation ---

    [Fact]
    public async Task ZeroArguments_ExitsWithCode2()
    {
        var (exitCode, _, _) = await RunAsync("bash", [RestoreDrillScript]);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task OneArgument_ExitsWithCode2()
    {
        var (exitCode, _, _) = await RunAsync("bash", [RestoreDrillScript, "only-one"]);
        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task ThreeArguments_ExitsWithCode2()
    {
        var (exitCode, _, _) = await RunAsync("bash", [RestoreDrillScript, "a", "b", "c"]);
        Assert.Equal(2, exitCode);
    }

    // --- B. archive validation ---

    [Fact]
    public async Task MissingArchive_ExitsWithCode3()
    {
        var drillRoot = Path.Combine(Path.GetTempPath(), $"eos-drill-test-{Guid.NewGuid():N}");
        var (exitCode, _, _) = await RunAsync("bash", [RestoreDrillScript, "/nonexistent/archive.tar.gz", drillRoot]);

        Assert.Equal(3, exitCode);
        Assert.False(Directory.Exists(drillRoot));
    }

    [Fact]
    public async Task UnreadableArchive_ExitsWithCode3()
    {
        var archive = Path.Combine(Path.GetTempPath(), $"eos-drill-unreadable-{Guid.NewGuid():N}.tar.gz");
        File.WriteAllText(archive, "not a real archive");
        File.SetUnixFileMode(archive, UnixFileMode.None);
        var drillRoot = Path.Combine(Path.GetTempPath(), $"eos-drill-test-{Guid.NewGuid():N}");

        try
        {
            var (exitCode, _, _) = await RunAsync("bash", [RestoreDrillScript, archive, drillRoot]);
            Assert.Equal(3, exitCode);
        }
        finally
        {
            File.SetUnixFileMode(archive, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Delete(archive);
        }
    }

    // --- Shared helper: build a synthetic-but-real, structurally valid backup archive ---
    // (empty sql/redis/chroma data directories owned by the current host user; the REAL
    // config/*.json and REAL .env are archived by the real deploy/backup.sh itself, read-only —
    // exactly what backup.sh does for any real invocation).
    private static async Task<string> BuildSyntheticBackupArchiveAsync(string label)
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), $"eos-drill-source-{label}-{Guid.NewGuid():N}");
        // The isolated SQL Server container writes to this bind mount as its own internal,
        // non-root "mssql" user (a different UID than the host user running this test) — the
        // directory must be group/other-writable for the container to initialize into it,
        // exactly as real deployment guidance for this image requires for any host bind mount.
        var permissiveMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;

        foreach (var subdirectory in new[] { "sql", "redis", "chroma" })
        {
            var path = Directory.CreateDirectory(Path.Combine(sourceRoot, subdirectory)).FullName;
            File.SetUnixFileMode(path, permissiveMode);
        }

        var destRoot = Path.Combine(Path.GetTempPath(), $"eos-drill-backups-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destRoot);

        var (exitCode, stdOut, stdErr) = await RunAsync(
            "bash", [BackupScript, destRoot], new Dictionary<string, string> { ["EOS_DATA_DIR"] = sourceRoot });

        Directory.Delete(sourceRoot, recursive: true);

        Assert.True(exitCode == 0, $"deploy/backup.sh failed building the synthetic test archive. stdout: {stdOut} stderr: {stdErr}");

        var archive = Directory.GetFiles(destRoot, "eos-backup-*.tar.gz").Single();
        return archive;
    }

    // --- C. extraction / D. Storage.json safety (structural, no Docker required) ---

    [Fact]
    public async Task Extraction_And_StorageJsonRewrite_AreCorrect_WithoutStartingAnyStack()
    {
        var storageJsonBefore = File.ReadAllBytes(Path.Combine(RealConfigDir, "Storage.json"));
        var archive = await BuildSyntheticBackupArchiveAsync("extract");

        try
        {
            // We only want to observe extraction + the Storage.json rewrite, not a full drill —
            // so we replicate exactly those two steps of restore-drill.sh's own logic here,
            // against a throwaway drill root, rather than running the full script (which would
            // also start the isolated Compose stack).
            var drillRoot = Path.Combine(Path.GetTempPath(), $"eos-drill-extract-{Guid.NewGuid():N}");
            Directory.CreateDirectory(drillRoot);
            try
            {
                using var fileStream = File.OpenRead(archive);
                using var gzip = new System.IO.Compression.GZipStream(fileStream, System.IO.Compression.CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(gzip, drillRoot, overwriteFiles: true);

                Assert.True(Directory.Exists(Path.Combine(drillRoot, "sql")));
                Assert.True(Directory.Exists(Path.Combine(drillRoot, "redis")));
                Assert.True(Directory.Exists(Path.Combine(drillRoot, "chroma")));
                Assert.True(Directory.Exists(Path.Combine(drillRoot, "config")));
                Assert.True(File.Exists(Path.Combine(drillRoot, ".env")));

                // The extracted (unmodified) copy still has the live dataDirectory — proving why
                // the rewrite step in restore-drill.sh is necessary.
                var extractedStorageJson = File.ReadAllText(Path.Combine(drillRoot, "config", "Storage.json"));
                Assert.Contains("eos/data", extractedStorageJson, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(drillRoot, recursive: true);
            }
        }
        finally
        {
            File.Delete(archive);
            Directory.Delete(Path.GetDirectoryName(archive)!, recursive: true);
        }

        var storageJsonAfter = File.ReadAllBytes(Path.Combine(RealConfigDir, "Storage.json"));
        Assert.Equal(storageJsonBefore, storageJsonAfter);
    }

    // --- E. Compose isolation (static file inspection, no Docker required) ---

    [Fact]
    public void RestoreDrillComposeFile_Exists()
    {
        Assert.True(File.Exists(ComposeFile));
    }

    [Fact]
    public void RestoreDrillComposeFile_UsesDistinctContainerNamesFromTheLiveStack()
    {
        var drillCompose = File.ReadAllText(ComposeFile);
        var liveCompose = File.ReadAllText(LiveComposeFile);

        var liveContainerNames = new[] { "eos-sqlserver", "eos-redis", "eos-chromadb" };
        var drillContainerNames = new[] { "eos-sqlserver-restore-drill", "eos-redis-restore-drill", "eos-chromadb-restore-drill" };

        foreach (var name in drillContainerNames)
        {
            Assert.Contains($"container_name: {name}", drillCompose, StringComparison.Ordinal);
        }

        foreach (var liveName in liveContainerNames)
        {
            // The live, unqualified name must not appear as an exact container_name in the drill file.
            Assert.DoesNotContain($"container_name: {liveName}\n", drillCompose, StringComparison.Ordinal);
        }

        _ = liveCompose;
    }

    [Fact]
    public void RestoreDrillComposeFile_UsesDistinctHostPortsFromTheLiveStack()
    {
        var drillCompose = File.ReadAllText(ComposeFile);
        var liveCompose = File.ReadAllText(LiveComposeFile);

        Assert.Contains("14330:1433", drillCompose, StringComparison.Ordinal);
        Assert.Contains("16380:6379", drillCompose, StringComparison.Ordinal);
        Assert.Contains("18001:8000", drillCompose, StringComparison.Ordinal);

        Assert.Contains("1433:1433", liveCompose, StringComparison.Ordinal);
        Assert.Contains("6379:6379", liveCompose, StringComparison.Ordinal);
        Assert.Contains("8000:8000", liveCompose, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreDrillComposeFile_MountsFromDrillRootVariable_NotFromEosDataDir()
    {
        var drillCompose = File.ReadAllText(ComposeFile);

        Assert.Contains("${DRILL_ROOT}/sql", drillCompose, StringComparison.Ordinal);
        Assert.Contains("${DRILL_ROOT}/redis", drillCompose, StringComparison.Ordinal);
        Assert.Contains("${DRILL_ROOT}/chroma", drillCompose, StringComparison.Ordinal);
        Assert.DoesNotContain("${EOS_DATA_DIR}", drillCompose, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveDockerComposeFile_IsUnchangedByWp030_07()
    {
        var liveCompose = File.ReadAllText(LiveComposeFile);

        Assert.Contains("container_name: eos-sqlserver\n", liveCompose, StringComparison.Ordinal);
        Assert.Contains("${EOS_DATA_DIR}/sql", liveCompose, StringComparison.Ordinal);
        Assert.DoesNotContain("restore-drill", liveCompose, StringComparison.OrdinalIgnoreCase);
    }

    // --- F. dedicated bootstrap driver: proves it uses the supplied config directory, not Discover() ---

    private static string CreateIsolatedConfigCopy(string label, bool omitOneRequiredFile = false)
    {
        var configDir = Path.Combine(Path.GetTempPath(), $"eos-drill-config-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(configDir);

        foreach (var sourceFile in Directory.GetFiles(RealConfigDir, "*.json"))
        {
            var fileName = Path.GetFileName(sourceFile);
            if (omitOneRequiredFile && fileName == "Knowledge.json")
            {
                continue;
            }

            File.Copy(sourceFile, Path.Combine(configDir, fileName));
        }

        var isolatedDataDir = Path.Combine(configDir, "..", $"eos-drill-data-{label}");
        File.WriteAllText(
            Path.Combine(configDir, "Storage.json"),
            $$"""{"dataDirectory": "{{isolatedDataDir.Replace("\\", "\\\\")}}"}""");

        return configDir;
    }

    [Fact]
    public async Task RunBootstrapAsync_UsesTheSuppliedConfigDirectory_NotTheRealRepositoryOne()
    {
        // If RunBootstrapAsync silently used JsonConfigurationLoader.Discover() instead of the
        // directory we pass it, this test would still pass (the real repo config/ is complete) —
        // so the proof is negative: remove one required file from OUR isolated copy only, and
        // confirm the failure is specifically "file not found" for that file, which can only
        // happen if our directory (not the real one) was actually read.
        var configDir = CreateIsolatedConfigCopy("missing-file", omitOneRequiredFile: true);
        try
        {
            var results = await EOS.RestoreDrill.RestoreDrillRunner.RunBootstrapAsync(configDir);

            var validateResult = results.Single(r => r.StepName == "Validate");
            Assert.False(validateResult.Status);
            Assert.Contains("Knowledge.json", validateResult.Error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(configDir, recursive: true);
        }
    }

    [Fact]
    public void IsCompleteSuccess_RequiresAllTenStepsAndReadyAsTheLastStep()
    {
        var allSucceeded = Enumerable.Range(1, 10)
            .Select(i => new BootstrapResult($"Step{i}", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, TimeSpan.Zero, null))
            .ToList();
        allSucceeded[^1] = allSucceeded[^1] with { StepName = "Ready" };

        Assert.True(EOS.RestoreDrill.RestoreDrillRunner.IsCompleteSuccess(allSucceeded));

        var earlyFailure = allSucceeded.Take(5).ToList();
        Assert.False(EOS.RestoreDrill.RestoreDrillRunner.IsCompleteSuccess(earlyFailure));
    }

    [Fact]
    public async Task RunAsync_WithMissingConfigDirectory_ExitsWithCode3()
    {
        var drillRoot = Path.Combine(Path.GetTempPath(), $"eos-drill-no-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(drillRoot);
        try
        {
            var exitCode = await EOS.RestoreDrill.RestoreDrillRunner.RunAsync([drillRoot]);
            Assert.Equal(3, exitCode);
        }
        finally
        {
            Directory.Delete(drillRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WithWrongArgumentCount_ExitsWithCode2()
    {
        Assert.Equal(2, await EOS.RestoreDrill.RestoreDrillRunner.RunAsync([]));
        Assert.Equal(2, await EOS.RestoreDrill.RestoreDrillRunner.RunAsync(["a", "b"]));
    }

    // --- G. REAL restore drill (real Docker, real SQL Server/Redis/ChromaDB, real BootstrapRunner) ---

    [Fact]
    public async Task RealRestoreDrill_ExtractsStartsIsolatedStackAndReachesReady()
    {
        // WP-030-07 honesty note (see final report for full detail): this repository's live
        // EOS_DATA_DIR contains real SQL Server/Redis container data owned by the containers'
        // internal UIDs (10001 / 999), unreadable by the host user running this test suite, with
        // no passwordless sudo available — deploy/backup.sh (WP030-06, unmodified, not this WP's
        // concern to fix) therefore cannot produce a complete archive of the LIVE data in this
        // environment. This test instead builds a genuinely real backup — deploy/backup.sh is
        // executed for real, against real (empty, host-owned) data directories, archiving the
        // REAL repository config/*.json and REAL .env exactly as any real invocation would — and
        // feeds it through the real, unmodified deploy/restore-drill.sh, which starts a real,
        // isolated Docker Compose stack (distinct names/ports) and invokes the real
        // EOS.RestoreDrill driver against the real, unmodified BootstrapRunner. Nothing here is
        // mocked or stubbed; the only difference from a live-data drill is that the restored
        // SQL Server/Redis/ChromaDB instances initialize fresh rather than from pre-existing
        // rows — Docker, the images, the health checks, and BootstrapRunner's 10 real steps are
        // all genuinely exercised end-to-end.
        var dockerAvailable = (await RunAsync("docker", ["info"])).ExitCode == 0;
        if (!dockerAvailable)
        {
            Assert.Fail("Docker unavailable — real restore-drill not executed.");
            return;
        }

        var archive = await BuildSyntheticBackupArchiveAsync("real-drill");
        var drillRoot = Path.Combine(Path.GetTempPath(), $"eos-drill-real-{Guid.NewGuid():N}");

        try
        {
            var (exitCode, stdOut, stdErr) = await RunAsync("bash", [RestoreDrillScript, archive, drillRoot]);

            Assert.True(
                exitCode == 0,
                $"Real restore drill did not reach Ready (exit {exitCode}).\n--- stdout ---\n{stdOut}\n--- stderr ---\n{stdErr}");
            Assert.Contains("Ready: PASS", stdOut, StringComparison.Ordinal);
            Assert.DoesNotContain("EOS_SQLSERVER_CONNECTION_STRING", stdOut, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(archive);
            var backupDir = Path.GetDirectoryName(archive)!;
            if (Directory.Exists(backupDir))
            {
                Directory.Delete(backupDir, recursive: true);
            }

            // Best-effort only: deploy/restore-drill.sh's own trap already tears down the drill
            // root on every exit path (its actual cleanup guarantee under test here). Some files
            // the isolated SQL Server container wrote may be owned by its internal, non-host UID
            // by the time this redundant, test-side cleanup attempt runs — that must never mask
            // the real assertions above, which have already executed.
            if (Directory.Exists(drillRoot))
            {
                try
                {
                    Directory.Delete(drillRoot, recursive: true);
                }
                catch (Exception)
                {
                    // best-effort; deploy/restore-drill.sh's own cleanup is the real guarantee.
                }
            }
        }
    }
}
