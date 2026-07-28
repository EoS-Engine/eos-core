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
        var protectionGate = new ProtectionGate(
            new PolicyEngine([], [], [], []), new RuleEngine(), new RiskEngine(), new ApprovalEngine(),
            new EmergencyShutdownState(),
            new ResourceCeilings(90, 8192, 476000, 100000, 32000, 4),
            NullLogger<ProtectionGate>.Instance);

        var connectionOptions = DataStoreConnectionOptions.FromEnvironment();
        var knowledgeGraphStore = new KnowledgeGraphStore(connectionOptions.SqlServerConnectionString);
        await knowledgeGraphStore.EnsureTableExistsAsync(CancellationToken.None);
        var capturingKnowledgeClient = new CapturingKnowledgeClient(new KnowledgeClient(knowledgeGraphStore));

        var askCommand = new AskCommand(reasoningEngine, protectionGate, capturingKnowledgeClient, NullLogger<AskCommand>.Instance);

        var exitCode = await askCommand.ExecuteAsync("explain the SOLID principles");

        Assert.Equal(0, exitCode);
        Assert.NotNull(capturingKnowledgeClient.LastUpdatedNodeId);
        var persisted = await knowledgeGraphStore.GetByIdAsync(capturingKnowledgeClient.LastUpdatedNodeId.Value, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(KnowledgeNodeType.Decision, persisted.NodeType);
        Assert.False(string.IsNullOrWhiteSpace(persisted.Content));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsNonZero_WhenTextIsEmpty()
    {
        var reasoningEngine = new ReasoningEngine(NeverCalledAIProviderClient.Instance);
        var protectionGate = new ProtectionGate(
            new PolicyEngine([], [], [], []), new RuleEngine(), new RiskEngine(), new ApprovalEngine(),
            new EmergencyShutdownState(),
            new ResourceCeilings(90, 8192, 476000, 100000, 32000, 4),
            NullLogger<ProtectionGate>.Instance);
        var askCommand = new AskCommand(
            reasoningEngine, protectionGate, NeverCalledKnowledgeClient.Instance, NullLogger<AskCommand>.Instance);

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

    private sealed class NeverCalledKnowledgeClient : IKnowledgeClient
    {
        public static readonly NeverCalledKnowledgeClient Instance = new();

        public Task UpdateAsync(
            Guid nodeId, KnowledgeNodeType nodeType, string content, string[] domainTags, string[] evidenceRefs,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Should not be called for an empty/whitespace request.");
        }
    }

    private sealed class CapturingKnowledgeClient(IKnowledgeClient inner) : IKnowledgeClient
    {
        public Guid? LastUpdatedNodeId { get; private set; }

        public Task UpdateAsync(
            Guid nodeId, KnowledgeNodeType nodeType, string content, string[] domainTags, string[] evidenceRefs,
            CancellationToken cancellationToken = default)
        {
            LastUpdatedNodeId = nodeId;
            return inner.UpdateAsync(nodeId, nodeType, content, domainTags, evidenceRefs, cancellationToken);
        }
    }
}
