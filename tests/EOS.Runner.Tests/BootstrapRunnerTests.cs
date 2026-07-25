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
}
