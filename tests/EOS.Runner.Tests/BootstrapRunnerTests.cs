using EOS.Runner.Bootstrap;
using Microsoft.Extensions.Logging.Abstractions;

namespace EOS.Runner.Tests;

public class BootstrapRunnerTests
{
    [Fact]
    public async Task RunAsync_ReachesReady_WhenRunTwiceConsecutively()
    {
        var loader = JsonConfigurationLoader.Discover();

        var firstRun = await BootstrapRunner.CreateEosBootstrap(loader, NullLogger<BootstrapRunner>.Instance).RunAsync();
        var secondRun = await BootstrapRunner.CreateEosBootstrap(loader, NullLogger<BootstrapRunner>.Instance).RunAsync();

        Assert.Equal(10, firstRun.Count);
        Assert.All(firstRun, r => Assert.True(r.Status));
        Assert.Equal(firstRun.Select(r => r.StepName), secondRun.Select(r => r.StepName));
        Assert.All(secondRun, r => Assert.True(r.Status));
    }

    [Fact]
    public async Task RunAsync_LastStepIsReady_WhenAllStepsSucceed()
    {
        var loader = JsonConfigurationLoader.Discover();

        var results = await BootstrapRunner.CreateEosBootstrap(loader, NullLogger<BootstrapRunner>.Instance).RunAsync();

        Assert.Equal("Ready", results[^1].StepName);
        Assert.True(results[^1].Status);
    }

    [Fact]
    public async Task RunAsync_FailsWithClearError_WhenARequiredConfigurationFileIsMissing()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("eos-bootstrap-tests-").FullName;
        try
        {
            // tempDirectory exists but contains none of the ten required configuration files.
            var loader = new JsonConfigurationLoader(tempDirectory);

            var results = await BootstrapRunner.CreateEosBootstrap(loader, NullLogger<BootstrapRunner>.Instance).RunAsync();

            var validateResult = results[^1];
            Assert.Equal("Validate", validateResult.StepName);
            Assert.False(validateResult.Status);
            Assert.Contains("Configuration file not found", validateResult.Error);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    // WP-022 CodeRabbit review Finding 1: the quota-rank-ordering validation
    // (Resource-Management-Specification-v1.0 §19.1) added to the "Health Check" step had no
    // regression test exercising its failure path. RunAsync() never throws to its caller — every
    // step's exception is caught internally and surfaced via that step's BootstrapResult.Error
    // (see the "missing configuration file" test above for the same, already-established
    // assertion pattern) — so this test asserts against the failed step's result, not
    // Assert.ThrowsAsync.
    [Fact]
    public async Task RunAsync_FailsAtHealthCheck_WhenAResourceClassQuotaViolatesRankOrdering()
    {
        var realConfigDirectory = JsonConfigurationLoader.Discover().ConfigDirectory;
        var tempDirectory = Directory.CreateTempSubdirectory("eos-bootstrap-tests-").FullName;
        try
        {
            foreach (var file in Directory.GetFiles(realConfigDirectory))
            {
                File.Copy(file, Path.Combine(tempDirectory, Path.GetFileName(file)));
            }

            // §16: LearningActivities (rank 5) must never have a larger Model-slot quota than
            // UserRequests (rank 1) — 10 > 3 is an intentional rank-ordering violation.
            var thresholdsPath = Path.Combine(tempDirectory, "Thresholds.json");
            var thresholds = await File.ReadAllTextAsync(thresholdsPath);
            thresholds = thresholds.Replace("\"modelSlotQuotaLearningActivitiesCount\": 1", "\"modelSlotQuotaLearningActivitiesCount\": 10");
            await File.WriteAllTextAsync(thresholdsPath, thresholds);

            var loader = new JsonConfigurationLoader(tempDirectory);

            var results = await BootstrapRunner.CreateEosBootstrap(loader, NullLogger<BootstrapRunner>.Instance).RunAsync();

            var healthCheckResult = results[^1];
            Assert.Equal("Health Check", healthCheckResult.StepName);
            Assert.False(healthCheckResult.Status);
            Assert.Contains("ModelSlotQuota", healthCheckResult.Error);
            Assert.Contains("non-increasing", healthCheckResult.Error);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
