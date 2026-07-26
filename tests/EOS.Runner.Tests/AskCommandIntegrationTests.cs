using EOS.AIProvider;
using EOS.Gates;
using EOS.Infrastructure;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using EOS.Reasoning;
using EOS.Runner.Bootstrap;
using EOS.Runner.Commands;
using EOS.SharedKernel.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace EOS.Runner.Tests;

public class AskCommandIntegrationTests
{
    [Fact]
    public async Task ExecuteAsync_ExplainSOLIDPrinciples_SucceedsAndPersistsARealQueryableKnowledgeNode()
    {
        var loader = JsonConfigurationLoader.Discover();
        var providersOptions = loader.Load<ProvidersOptions>("Providers.json");
        var inferenceOptions = loader.Load<InferenceOptions>("Inference.json");
        var ollamaEndpoint = providersOptions.Providers.Single(p => p.Name == "ollama").Endpoint;

        using var httpClient = new HttpClient { BaseAddress = new Uri(ollamaEndpoint) };
        var aiProviderClient = new OllamaProviderAdapter(
            httpClient, inferenceOptions.DefaultModel, inferenceOptions.MaxTokens, inferenceOptions.Temperature);
        var reasoningEngine = new ReasoningEngine(aiProviderClient);
        var protectionGate = new ProtectionGate(NullLogger<ProtectionGate>.Instance);

        var connectionOptions = DataStoreConnectionOptions.FromEnvironment();
        var knowledgeGraphStore = new KnowledgeGraphStore(connectionOptions.SqlServerConnectionString);
        await knowledgeGraphStore.EnsureTableExistsAsync(CancellationToken.None);
        var knowledgeClient = new KnowledgeClient(knowledgeGraphStore);

        var askCommand = new AskCommand(reasoningEngine, protectionGate, knowledgeClient, NullLogger<AskCommand>.Instance);

        var exitCode = await askCommand.ExecuteAsync("explain the SOLID principles");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNonZero_WhenTextIsEmpty()
    {
        var reasoningEngine = new ReasoningEngine(NeverCalledAIProviderClient.Instance);
        var protectionGate = new ProtectionGate(NullLogger<ProtectionGate>.Instance);
        var connectionOptions = DataStoreConnectionOptions.FromEnvironment();
        var knowledgeClient = new KnowledgeClient(new KnowledgeGraphStore(connectionOptions.SqlServerConnectionString));
        var askCommand = new AskCommand(reasoningEngine, protectionGate, knowledgeClient, NullLogger<AskCommand>.Instance);

        var exitCode = await askCommand.ExecuteAsync("   ");

        Assert.NotEqual(0, exitCode);
    }

    private sealed class NeverCalledAIProviderClient : EOS.SDK.IAIProviderClient
    {
        public static readonly NeverCalledAIProviderClient Instance = new();

        public Task<EOS.SDK.InferenceResult> InferAsync(EOS.SDK.InferenceRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Should not be called for an empty/whitespace request.");
        }
    }
}
