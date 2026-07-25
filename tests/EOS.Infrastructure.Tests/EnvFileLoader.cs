namespace EOS.Infrastructure.Tests;

/// <summary>
/// Test-only helper that loads the repository's local .env file into the process
/// environment, so integration tests see the same variables EOS.Runner would get
/// from a real shell. Not part of EOS.Infrastructure's production configuration path.
/// </summary>
internal static class EnvFileLoader
{
    private static readonly object LoadLock = new();
    private static bool _loaded;

    public static void EnsureLoaded()
    {
        lock (LoadLock)
        {
            if (_loaded)
            {
                return;
            }

            var envPath = Path.Combine(FindRepositoryRoot(), ".env");
            if (File.Exists(envPath))
            {
                foreach (var line in File.ReadAllLines(envPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                    {
                        continue;
                    }

                    var separatorIndex = trimmed.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    var key = trimmed[..separatorIndex];
                    var value = trimmed[(separatorIndex + 1)..];
                    Environment.SetEnvironmentVariable(key, value);
                }
            }

            _loaded = true;
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EOS.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root (EOS.slnx not found).");
    }
}
