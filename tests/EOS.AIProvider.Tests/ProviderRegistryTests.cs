using EOS.AIProvider;

namespace EOS.AIProvider.Tests;

public class ProviderRegistryTests
{
    private static ProviderProfile CreateProvider(string name, int priority, params string[] capabilities)
    {
        return new ProviderProfile(name, $"http://localhost/{name}", priority, [new ModelProfile($"{name}-model", capabilities)]);
    }

    [Fact]
    public void FindByCapability_ReturnsOnlyProvidersWhoseModelsSupportTheCapability()
    {
        var chatProvider = CreateProvider("chat-provider", priority: 1, "Chat");
        var visionProvider = CreateProvider("vision-provider", priority: 2, "Vision");
        var registry = new ProviderRegistry([chatProvider, visionProvider]);

        var result = registry.FindByCapability("Chat");

        var found = Assert.Single(result);
        Assert.Equal("chat-provider", found.Name);
    }

    [Fact]
    public void FindByCapability_IsCaseInsensitive()
    {
        var provider = CreateProvider("chat-provider", priority: 1, "Chat");
        var registry = new ProviderRegistry([provider]);

        var result = registry.FindByCapability("chat");

        Assert.Single(result);
    }

    [Fact]
    public void FindByCapability_ReturnsEmpty_WhenNoProviderSupportsTheCapability()
    {
        var provider = CreateProvider("chat-provider", priority: 1, "Chat");
        var registry = new ProviderRegistry([provider]);

        var result = registry.FindByCapability("Vision");

        Assert.Empty(result);
    }
}
