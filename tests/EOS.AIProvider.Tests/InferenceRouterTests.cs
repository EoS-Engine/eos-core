using EOS.AIProvider;

namespace EOS.AIProvider.Tests;

public class InferenceRouterTests
{
    private static ProviderProfile CreateProvider(string name, int priority, params string[] capabilities)
    {
        return new ProviderProfile(name, $"http://localhost/{name}", priority, [new ModelProfile($"{name}-model", capabilities)]);
    }

    private static HealthMonitor CreateHealthMonitor()
    {
        return new HealthMonitor(new HealthThresholds(3, TimeSpan.FromSeconds(30)), new NoOpProviderEventLogger());
    }

    [Fact]
    public void Route_ExcludesProvidersThatDoNotSupportTheRequestedCapability()
    {
        var chatProvider = CreateProvider("chat-provider", priority: 1, "Chat");
        var visionProvider = CreateProvider("vision-provider", priority: 2, "Vision");
        var registry = new ProviderRegistry([chatProvider, visionProvider]);
        var router = new InferenceRouter(registry, CreateHealthMonitor());

        var candidates = router.Route("Chat");

        var candidate = Assert.Single(candidates);
        Assert.Equal("chat-provider", candidate.Provider.Name);
    }

    [Fact]
    public void Route_RanksCandidatesByAscendingPriority()
    {
        var lowerPriorityProvider = CreateProvider("secondary", priority: 2, "Chat");
        var higherPriorityProvider = CreateProvider("primary", priority: 1, "Chat");
        var registry = new ProviderRegistry([lowerPriorityProvider, higherPriorityProvider]);
        var router = new InferenceRouter(registry, CreateHealthMonitor());

        var candidates = router.Route("Chat");

        Assert.Equal(["primary", "secondary"], candidates.Select(c => c.Provider.Name));
    }

    [Fact]
    public void Route_ExcludesProvidersThatHealthMonitorHasMarkedUnavailable()
    {
        var healthMonitor = CreateHealthMonitor();
        var provider = CreateProvider("chat-provider", priority: 1, "Chat");
        var registry = new ProviderRegistry([provider]);
        var router = new InferenceRouter(registry, healthMonitor);

        healthMonitor.RecordFailure("chat-provider");
        healthMonitor.RecordFailure("chat-provider");
        healthMonitor.RecordFailure("chat-provider");

        var candidates = router.Route("Chat");

        Assert.Empty(candidates);
    }
}
