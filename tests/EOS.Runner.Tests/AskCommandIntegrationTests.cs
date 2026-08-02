using EOS.AIProvider;
using EOS.Contracts;
using EOS.Gates;
using EOS.Infrastructure;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using EOS.Reasoning;
using EOS.Runner.Bootstrap;
using EOS.Runner.Commands;
using EOS.SharedKernel.Configuration;
using EOS.VectorStore;
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
        var reasoningEngine = new ReasoningEngine(
            aiProviderClient,
            NeverCalledContextAcquisitionProvider.Instance,
            new ReasoningEngineOptions(ContextExpansionCap: 1, LowConfidenceFloor: 0.3),
            new NoOpDecisionMadeEventPublisher(),
            new NoOpLowConfidenceDecisionFlaggedEventPublisher(),
            new NoOpContextExpansionRequestedEventPublisher(),
            NullLogger<ReasoningEngine>.Instance);
        var protectionGate = new ProtectionGate(
            new PolicyEngine([], [], [], []), new RuleEngine(), new RiskEngine(), new ApprovalEngine(),
            new EmergencyShutdownState(),
            new ResourceCeilings(90, 8192, 476000, 100000, 32000, 4),
            NullLogger<ProtectionGate>.Instance);

        var connectionOptions = DataStoreConnectionOptions.FromEnvironment();
        var knowledgeGraphStore = new KnowledgeGraphStore(connectionOptions.SqlServerConnectionString);
        await knowledgeGraphStore.EnsureTableExistsAsync(CancellationToken.None);
        var rankingWeights = new RankingWeights(
            VectorSimilarity: 0.4, Recency: 0.3, DomainMatch: 0.2, AccessFrequency: 0.1);
        var capturingKnowledgeClient =
            new CapturingKnowledgeClient(new KnowledgeClient(
                knowledgeGraphStore,
                rankingWeights,
                new ChromaVectorStore(connectionOptions.ChromaDbEndpoint),
                NeverCalledMemorySourceStore.Instance));

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
        var reasoningEngine = new ReasoningEngine(
            NeverCalledAIProviderClient.Instance,
            NeverCalledContextAcquisitionProvider.Instance,
            new ReasoningEngineOptions(ContextExpansionCap: 1, LowConfidenceFloor: 0.3),
            new NoOpDecisionMadeEventPublisher(),
            new NoOpLowConfidenceDecisionFlaggedEventPublisher(),
            new NoOpContextExpansionRequestedEventPublisher(),
            NullLogger<ReasoningEngine>.Instance);
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

    private sealed class NeverCalledContextAcquisitionProvider : IContextAcquisitionProvider
    {
        public static readonly NeverCalledContextAcquisitionProvider Instance = new();

        public Task<AcquiredContext> AcquireContextAsync(
            ReasoningContextScope scope, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Should not be called when ReasoningRequest.ContextScope is null.");
        }
    }

    private sealed class NoOpDecisionMadeEventPublisher : IDecisionMadeEventPublisher
    {
        public void PublishDecisionMade(Guid decisionId, Guid requestId, double confidence, double riskScore, ReasoningType reasoningTypeApplied)
        {
        }
    }

    private sealed class NoOpLowConfidenceDecisionFlaggedEventPublisher : ILowConfidenceDecisionFlaggedEventPublisher
    {
        public void PublishLowConfidenceDecisionFlagged(Guid decisionId, Guid correlationId, double confidence, double threshold)
        {
        }
    }

    private sealed class NoOpContextExpansionRequestedEventPublisher : IContextExpansionRequestedEventPublisher
    {
        public void PublishContextExpansionRequested(Guid requestId, ReasoningContextScope originalScope, ReasoningContextScope expandedScope)
        {
        }
    }

    private sealed class NeverCalledKnowledgeClient : IKnowledgeClient
    {
        public static readonly NeverCalledKnowledgeClient Instance = new();

        public Task UpdateAsync(
            Guid nodeId, KnowledgeNodeType nodeType, string content, string[] domainTags, string[] evidenceRefs,
            KnowledgeMetadata? metadata = null, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Should not be called for an empty/whitespace request.");
        }

        public Task<IEnumerable<KnowledgeNode>> QueryAsync(
            MemoryType? type, string[]? domainTags, DateRange? range, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Should not be called for an empty/whitespace request.");
        }

        public Task<IEnumerable<KnowledgeNode>> QuerySimilarAsync(Guid nodeId, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Should not be called for an empty/whitespace request.");
        }

        public Task<ContextPayload> AssembleContextAsync(ContextRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Should not be called for an empty/whitespace request.");
        }

        public Task<Guid> ConsolidateAsync(
            MemoryRef source, string reason, string[] evidenceRefs, bool suppressLessonLearned = false,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Should not be called for an empty/whitespace request.");
        }
    }

    private sealed class NeverCalledMemorySourceStore : IMemorySourceStore
    {
        public static readonly NeverCalledMemorySourceStore Instance = new();

        public Task<string?> GetContentAsync(MemoryRef source, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Should not be called by this test.");
        }

        public Task<bool> IsConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Should not be called by this test.");
        }

        public Task MarkConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Should not be called by this test.");
        }
    }

    private sealed class CapturingKnowledgeClient(IKnowledgeClient inner) : IKnowledgeClient
    {
        public Guid? LastUpdatedNodeId { get; private set; }

        public Task UpdateAsync(
            Guid nodeId, KnowledgeNodeType nodeType, string content, string[] domainTags, string[] evidenceRefs,
            KnowledgeMetadata? metadata = null, CancellationToken cancellationToken = default)
        {
            LastUpdatedNodeId = nodeId;
            return inner.UpdateAsync(nodeId, nodeType, content, domainTags, evidenceRefs, metadata, cancellationToken);
        }

        public Task<IEnumerable<KnowledgeNode>> QueryAsync(
            MemoryType? type, string[]? domainTags, DateRange? range, CancellationToken cancellationToken = default)
        {
            return inner.QueryAsync(type, domainTags, range, cancellationToken);
        }

        public Task<IEnumerable<KnowledgeNode>> QuerySimilarAsync(Guid nodeId, CancellationToken cancellationToken = default)
        {
            return inner.QuerySimilarAsync(nodeId, cancellationToken);
        }

        public Task<ContextPayload> AssembleContextAsync(ContextRequest request, CancellationToken cancellationToken = default)
        {
            return inner.AssembleContextAsync(request, cancellationToken);
        }

        public Task<Guid> ConsolidateAsync(
            MemoryRef source, string reason, string[] evidenceRefs, bool suppressLessonLearned = false,
            CancellationToken cancellationToken = default)
        {
            return inner.ConsolidateAsync(source, reason, evidenceRefs, suppressLessonLearned, cancellationToken);
        }
    }
}
