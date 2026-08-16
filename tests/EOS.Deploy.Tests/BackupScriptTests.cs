using System.Diagnostics;
using System.Formats.Tar;
using System.Runtime.Versioning;

// deploy/backup.sh is bash/Unix-only (chmod, tar, GNU date); these tests shell out to it and
// inspect Unix file modes, so this assembly is declared Linux-only rather than suppressing the
// platform-compatibility analyzer.
[assembly: SupportedOSPlatform("linux")]

namespace EOS.Deploy.Tests;

// WP-030-06: deterministic subprocess tests for deploy/backup.sh. Every test builds its own,
// fully isolated fake "repo" (deploy/backup.sh copied in, plus a fake config/ and .env) and a
// fake EOS_DATA_DIR — the script derives its own repo root from its own script path, so running
// the copy from an isolated temp tree never touches the real repository's config/.env, the real
// EOS_DATA_DIR, or any real infrastructure. Every temp tree is deleted in a try/finally.
public class BackupScriptTests
{
    private static readonly string RealScriptPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "deploy", "backup.sh"));

    private sealed class IsolatedEnvironment : IDisposable
    {
        public string Root { get; }
        public string DataDir { get; }
        public string Destination { get; }
        public string ScriptPath { get; }

        public IsolatedEnvironment()
        {
            Root = Directory.CreateTempSubdirectory("eos-backup-test-").FullName;
            DataDir = Path.Combine(Root, "data");
            Destination = Path.Combine(Root, "dest");

            Directory.CreateDirectory(Path.Combine(DataDir, "sql"));
            Directory.CreateDirectory(Path.Combine(DataDir, "redis"));
            Directory.CreateDirectory(Path.Combine(DataDir, "chroma"));
            File.WriteAllText(Path.Combine(DataDir, "sql", "db.mdf"), "sql-data");
            File.WriteAllText(Path.Combine(DataDir, "redis", "dump.rdb"), "redis-data");
            File.WriteAllText(Path.Combine(DataDir, "chroma", "index.bin"), "chroma-data");

            var repoDir = Path.Combine(Root, "repo");
            Directory.CreateDirectory(Path.Combine(repoDir, "config"));
            Directory.CreateDirectory(Path.Combine(repoDir, "deploy"));
            File.WriteAllText(Path.Combine(repoDir, "config", "Dashboard.json"), """{"title":"test"}""");
            File.WriteAllText(Path.Combine(repoDir, ".env"), "EOS_SECRET_TEST_VALUE=do-not-print-me");

            ScriptPath = Path.Combine(repoDir, "deploy", "backup.sh");
            File.Copy(RealScriptPath, ScriptPath);
            File.SetUnixFileMode(ScriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            Directory.CreateDirectory(Destination);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // best-effort cleanup; temp directories under /tmp are reclaimed by the OS regardless
            }
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunScriptAsync(
        string scriptPath, string? dataDir, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (dataDir is not null)
        {
            startInfo.Environment["EOS_DATA_DIR"] = dataDir;
        }
        else
        {
            startInfo.Environment.Remove("EOS_DATA_DIR");
        }

        using var process = Process.Start(startInfo)!;
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdOutTask, await stdErrTask);
    }

    private static List<string> ReadArchiveMembers(string archivePath)
    {
        using var fileStream = File.OpenRead(archivePath);
        using var gzip = new System.IO.Compression.GZipStream(fileStream, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new TarReader(gzip);

        var members = new List<string>();
        while (reader.GetNextEntry() is { } entry)
        {
            members.Add(entry.Name);
        }

        return members;
    }

    // --- A. Successful backup ---

    [Fact]
    public async Task SuccessfulBackup_CreatesExactlyOneArchiveWithExpectedMembers()
    {
        using var env = new IsolatedEnvironment();

        var (exitCode, _, stdErr) = await RunScriptAsync(env.ScriptPath, env.DataDir, env.Destination);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stdErr.Trim());

        var archives = Directory.GetFiles(env.Destination, "eos-backup-*.tar.gz");
        var archive = Assert.Single(archives);

        var members = ReadArchiveMembers(archive);
        Assert.Contains(members, m => m.StartsWith("sql/", StringComparison.Ordinal));
        Assert.Contains(members, m => m.StartsWith("redis/", StringComparison.Ordinal));
        Assert.Contains(members, m => m.StartsWith("chroma/", StringComparison.Ordinal));
        Assert.Contains(members, m => m.StartsWith("config/", StringComparison.Ordinal));
        Assert.Contains(".env", members);

        // WP030-06: archive-internal paths must be normalized, never embedding the host's
        // absolute EOS_DATA_DIR/repo path.
        Assert.DoesNotContain(members, m => m.Contains(env.Root, StringComparison.Ordinal));
    }

    // --- B. Security ---

    [Fact]
    public async Task SuccessfulBackup_ArchivePermissionsAreOwnerReadWriteOnly()
    {
        using var env = new IsolatedEnvironment();

        await RunScriptAsync(env.ScriptPath, env.DataDir, env.Destination);

        var archive = Directory.GetFiles(env.Destination, "eos-backup-*.tar.gz").Single();
        var mode = File.GetUnixFileMode(archive);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [Fact]
    public async Task SuccessfulBackup_NeverPrintsEnvContentsToStdOutOrStdErr()
    {
        using var env = new IsolatedEnvironment();

        var (_, stdOut, stdErr) = await RunScriptAsync(env.ScriptPath, env.DataDir, env.Destination);

        Assert.DoesNotContain("do-not-print-me", stdOut, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-print-me", stdErr, StringComparison.Ordinal);
    }

    // --- C. Argument validation ---

    [Fact]
    public async Task ZeroArguments_ExitsWithCode2()
    {
        using var env = new IsolatedEnvironment();

        var (exitCode, _, _) = await RunScriptAsync(env.ScriptPath, env.DataDir);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task TwoArguments_ExitsWithCode2()
    {
        using var env = new IsolatedEnvironment();

        var (exitCode, _, _) = await RunScriptAsync(env.ScriptPath, env.DataDir, env.Destination, "extra-argument");

        Assert.Equal(2, exitCode);
    }

    // --- D. Missing source validation ---

    [Theory]
    [InlineData("sql")]
    [InlineData("redis")]
    [InlineData("chroma")]
    public async Task MissingDataSubdirectory_ExitsWithCode3(string subdirectory)
    {
        using var env = new IsolatedEnvironment();
        Directory.Delete(Path.Combine(env.DataDir, subdirectory), recursive: true);

        var (exitCode, _, _) = await RunScriptAsync(env.ScriptPath, env.DataDir, env.Destination);

        Assert.Equal(3, exitCode);
        Assert.Empty(Directory.GetFiles(env.Destination, "eos-backup-*.tar.gz"));
    }

    [Fact]
    public async Task MissingConfigDirectory_ExitsWithCode3()
    {
        using var env = new IsolatedEnvironment();
        Directory.Delete(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(env.ScriptPath))!, "config"), recursive: true);

        var (exitCode, _, _) = await RunScriptAsync(env.ScriptPath, env.DataDir, env.Destination);

        Assert.Equal(3, exitCode);
    }

    [Fact]
    public async Task MissingEnvFile_ExitsWithCode3()
    {
        using var env = new IsolatedEnvironment();
        File.Delete(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(env.ScriptPath))!, ".env"));

        var (exitCode, _, _) = await RunScriptAsync(env.ScriptPath, env.DataDir, env.Destination);

        Assert.Equal(3, exitCode);
    }

    [Fact]
    public async Task MissingEosDataDirEnvironmentVariable_ExitsWithCode3()
    {
        using var env = new IsolatedEnvironment();

        var (exitCode, _, _) = await RunScriptAsync(env.ScriptPath, dataDir: null, env.Destination);

        Assert.Equal(3, exitCode);
    }

    // --- E. Retention ---

    [Fact]
    public async Task Retention_KeepsExactlyTheApprovedSevenDailyFourWeeklySet()
    {
        using var env = new IsolatedEnvironment();
        var today = DateTime.UtcNow.Date;

        string SeedArchive(int daysAgo)
        {
            var name = $"eos-backup-{today.AddDays(-daysAgo):yyyyMMdd}-010000.tar.gz";
            var path = Path.Combine(env.Destination, name);
            File.WriteAllText(path, "seed");
            return name;
        }

        // Last-7-days set (age 0-6): kept.
        var keepDaily = new[] { SeedArchive(0), SeedArchive(3), SeedArchive(6) };
        // The literal gap at age 7 (neither "last 7 days" nor bucket 1's 8-14 range): pruned.
        var gapDay = SeedArchive(7);
        // Bucket 1 (8-14 ago): only the most recent (age 9) survives.
        var bucket1Newest = SeedArchive(9);
        var bucket1Older = new[] { SeedArchive(12), SeedArchive(14) };
        // Bucket 2 (15-21 ago): sole candidate survives.
        var bucket2Only = SeedArchive(20);
        // Bucket 4 (29-35 ago): sole candidate survives (bucket 3 deliberately left empty).
        var bucket4Only = SeedArchive(30);
        // Beyond all buckets (36 ago): pruned.
        var beyondAllBuckets = SeedArchive(36);

        await RunScriptAsync(env.ScriptPath, env.DataDir, env.Destination);

        var remaining = Directory.GetFiles(env.Destination, "eos-backup-*.tar.gz")
            .Select(Path.GetFileName)
            .ToHashSet();

        foreach (var expectedKept in keepDaily.Concat([bucket1Newest, bucket2Only, bucket4Only]))
        {
            Assert.Contains(expectedKept, remaining);
        }

        foreach (var expectedPruned in bucket1Older.Concat([gapDay, beyondAllBuckets]))
        {
            Assert.DoesNotContain(expectedPruned, remaining);
        }

        // Plus the archive the run itself just created.
        Assert.Equal(keepDaily.Length + 1 /* bucket1 */ + 1 /* bucket2 */ + 1 /* bucket4 */ + 1 /* today's new archive */, remaining.Count);
    }

    // --- Hardening fix: retention must ignore unrelated files rather than crash or delete them ---

    [Fact]
    public async Task Retention_IgnoresAndNeverDeletesAFileThatDoesNotMatchTheBackupNamingConvention()
    {
        using var env = new IsolatedEnvironment();
        var unrelatedFile = Path.Combine(env.Destination, "eos-backup-manual.tar.gz");
        File.WriteAllText(unrelatedFile, "not produced by this script");

        var (exitCode, _, stdErr) = await RunScriptAsync(env.ScriptPath, env.DataDir, env.Destination);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, stdErr.Trim());
        Assert.True(File.Exists(unrelatedFile));
    }

    // --- F. Repeated execution ---

    [Fact]
    public async Task RepeatedExecution_NeverOverwritesAnExistingArchive()
    {
        using var env = new IsolatedEnvironment();

        var (exitCode1, _, _) = await RunScriptAsync(env.ScriptPath, env.DataDir, env.Destination);
        var firstArchive = Directory.GetFiles(env.Destination, "eos-backup-*.tar.gz").Single();
        var firstContentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(firstArchive)));

        var (exitCode2, _, _) = await RunScriptAsync(env.ScriptPath, env.DataDir, env.Destination);

        Assert.Equal(0, exitCode1);
        Assert.Equal(0, exitCode2);

        var archivesAfterSecondRun = Directory.GetFiles(env.Destination, "eos-backup-*.tar.gz");

        // Same-second collisions are handled via a numeric suffix (see backup.sh) rather than an
        // overwrite — either a second distinct file exists, or (if the runs landed in different
        // seconds) the first archive's bytes are provably untouched.
        if (archivesAfterSecondRun.Length == 1)
        {
            var untouchedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(firstArchive)));
            Assert.Equal(firstContentHash, untouchedHash);
        }
        else
        {
            Assert.True(archivesAfterSecondRun.Length >= 2);
            Assert.Contains(firstArchive, archivesAfterSecondRun);
        }
    }
}
