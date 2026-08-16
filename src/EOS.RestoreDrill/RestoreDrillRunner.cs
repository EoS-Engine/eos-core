using System.Text.RegularExpressions;
using EOS.Runner.Bootstrap;
using Microsoft.Extensions.Logging.Abstractions;

namespace EOS.RestoreDrill;

/// <summary>
/// WP-030-07: the dedicated restore-drill driver. Reuses the existing, unmodified
/// <see cref="BootstrapRunner"/> and <see cref="JsonConfigurationLoader"/> exactly as WP-002
/// already built them — no bootstrap logic is duplicated or reimplemented here. Deliberately
/// constructs <see cref="JsonConfigurationLoader"/> directly from the supplied, already-isolated
/// config directory rather than calling <see cref="JsonConfigurationLoader.Discover"/>, which
/// would resolve the real repository's <c>config/</c> directory instead of the restored one.
/// </summary>
public static class RestoreDrillRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: EOS.RestoreDrill <drill-root>");
            return 2;
        }

        var drillRoot = args[0];
        var configDirectory = Path.Combine(drillRoot, "config");

        if (!Directory.Exists(configDirectory))
        {
            Console.Error.WriteLine($"Config directory not found: {configDirectory}");
            return 3;
        }

        var results = await RunBootstrapAsync(configDirectory);

        foreach (var result in results)
        {
            Console.WriteLine(
                $"{result.StepName}: {(result.Status ? "PASS" : "FAIL")}" +
                (result.Error is not null ? $" - {RedactSensitiveContent(result.Error)}" : string.Empty));
        }

        return IsCompleteSuccess(results) ? 0 : 1;
    }

    // BootstrapRunner's "Start Infrastructure" step surfaces raw exceptions from SqlConnection /
    // ConnectionMultiplexer, whose messages can echo the connection string they failed to open —
    // including the SA password. This strips any password=/pwd= value (case-insensitive) before
    // the message is ever written to stdout.
    private static readonly Regex SensitiveKeyValuePattern = new(
        @"(?<key>password|pwd)\s*=\s*[^;]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? RedactSensitiveContent(string? message) =>
        message is null ? null : SensitiveKeyValuePattern.Replace(message, "${key}=***");

    public static Task<IReadOnlyList<BootstrapResult>> RunBootstrapAsync(string configDirectory)
    {
        var configurationLoader = new JsonConfigurationLoader(configDirectory);
        var runner = BootstrapRunner.CreateEosBootstrap(configurationLoader, NullLogger<BootstrapRunner>.Instance);
        return runner.RunAsync();
    }

    public static bool IsCompleteSuccess(IReadOnlyList<BootstrapResult> results) =>
        results.Count == 10 && results.All(r => r.Status) && results[^1].StepName == "Ready";
}
