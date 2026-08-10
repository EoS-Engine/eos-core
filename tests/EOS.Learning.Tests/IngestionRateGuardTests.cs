namespace EOS.Learning.Tests;

public class IngestionRateGuardTests
{
    [Fact]
    public async Task ExceedsThresholdAsync_ReturnsFalse_WhenCountIsBelowThreshold()
    {
        var guard = new IngestionRateGuard(new FixedCountIngestionRateGuardStore(fixedCount: 50), windowSeconds: 3600, thresholdCount: 100);

        var result = await guard.ExceedsThresholdAsync("role-a", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task ExceedsThresholdAsync_ReturnsFalse_WhenCountIsExactlyAtThreshold()
    {
        var guard = new IngestionRateGuard(new FixedCountIngestionRateGuardStore(fixedCount: 100), windowSeconds: 3600, thresholdCount: 100);

        var result = await guard.ExceedsThresholdAsync("role-a", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task ExceedsThresholdAsync_ReturnsTrue_WhenCountIsAboveThreshold()
    {
        var guard = new IngestionRateGuard(new FixedCountIngestionRateGuardStore(fixedCount: 101), windowSeconds: 3600, thresholdCount: 100);

        var result = await guard.ExceedsThresholdAsync("role-a", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task ExceedsThresholdAsync_TracksEachProducerRoleIndependently()
    {
        var store = new InMemoryIngestionRateGuardStore();
        var guard = new IngestionRateGuard(store, windowSeconds: 3600, thresholdCount: 2);

        Assert.False(await guard.ExceedsThresholdAsync("role-a", CancellationToken.None)); // role-a: 1
        Assert.False(await guard.ExceedsThresholdAsync("role-a", CancellationToken.None)); // role-a: 2
        Assert.True(await guard.ExceedsThresholdAsync("role-a", CancellationToken.None));  // role-a: 3, exceeds

        Assert.False(await guard.ExceedsThresholdAsync("role-b", CancellationToken.None)); // role-b: 1, unaffected by role-a
    }
}
