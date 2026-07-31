namespace EOS.Knowledge.Tests;

public class MemoryExpirationPolicyTests
{
    private readonly MemoryExpirationPolicy _policy = new(shortTermExpirationSeconds: 3600, sessionIdleTimeoutSeconds: 1800);

    [Fact]
    public void GetExpiration_ReturnsTheConfiguredShortTermSeconds()
    {
        var result = _policy.GetExpiration(MemoryType.ShortTerm);

        Assert.Equal(TimeSpan.FromSeconds(3600), result);
    }

    [Fact]
    public void GetExpiration_ReturnsTheConfiguredSessionIdleTimeout()
    {
        var result = _policy.GetExpiration(MemoryType.Session);

        Assert.Equal(TimeSpan.FromSeconds(1800), result);
    }

    [Theory]
    [InlineData(MemoryType.Working)]
    [InlineData(MemoryType.Episodic)]
    [InlineData(MemoryType.Semantic)]
    [InlineData(MemoryType.LongTerm)]
    [InlineData(MemoryType.Project)]
    public void GetExpiration_ReturnsNull_ForMemoryTypesThatNeverAutomaticallyExpire(MemoryType memoryType)
    {
        var result = _policy.GetExpiration(memoryType);

        Assert.Null(result);
    }
}
