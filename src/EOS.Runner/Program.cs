using EOS.AIProvider;
using EOS.Gates;
using EOS.Infrastructure;
using EOS.Contracts;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using EOS.Orchestrator;
using EOS.Reasoning;
using EOS.Runner.Bootstrap;
using EOS.Runner.Commands;
using EOS.SDK;
using EOS.SharedKernel.Configuration;
using EOS.VectorStore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateApplicationBuilder(args).Build();
var bootstrapLogger = host.Services.GetRequiredService<ILogger<BootstrapRunner>>();

var loader = JsonConfigurationLoader.Discover();
var runner = BootstrapRunner.CreateEosBootstrap(loader, bootstrapLogger);
var results = await runner.RunAsync();

if (results.Count == 0 || !results.All(r => r.Status))
{
    return 1;
}

if (args is not ["ask", var text])
{
    return 0;
}

var providersOptions = loader.Load<ProvidersOptions>("Providers.json");
var inferenceOptions = loader.Load<InferenceOptions>("Inference.json");
var thresholdsOptions = loader.Load<ThresholdsOptions>("Thresholds.json");

var providerProfiles = providersOptions.Providers
    .Select(provider => new ProviderProfile(
        provider.Name,
        provider.Endpoint,
        provider.Priority,
        provider.Models.Select(model => new ModelProfile(model.Name, model.Capabilities)).ToList()))
    .ToList();

var httpClients = providersOptions.Providers.Select(provider => new HttpClient
{
    BaseAddress = new Uri(provider.Endpoint),
    Timeout = TimeSpan.FromSeconds(thresholdsOptions.InferenceTimeoutSeconds),
}).ToList();

try
{
    var adapters = providersOptions.Providers.Zip(httpClients)
        .SelectMany(pair =>
        {
            var (provider, httpClient) = pair;
            var models = provider.Models.Count > 0
                ? provider.Models.Select(model => model.Name)
                : [inferenceOptions.DefaultModel];

            return models.Select(modelName =>
            {
                IAIProviderClient adapter = new OllamaProviderAdapter(
                    httpClient, modelName, inferenceOptions.MaxTokens, inferenceOptions.Temperature);
                return ((provider.Name, modelName), adapter);
            });
        })
        .ToDictionary(x => x.Item1, x => x.adapter);

    var providerRegistry = new ProviderRegistry(providerProfiles);
    var healthThresholds = new HealthThresholds(
        thresholdsOptions.ProviderFailureThreshold, TimeSpan.FromSeconds(thresholdsOptions.ProviderRecoveryProbeIntervalSeconds));
    var healthMonitor = new HealthMonitor(
        healthThresholds, new LoggerProviderEventLogger(host.Services.GetRequiredService<ILogger<HealthMonitor>>()));
    var inferenceRouter = new InferenceRouter(providerRegistry, healthMonitor);
    var aiProviderClient = new AIProviderManager(
        inferenceRouter,
        healthMonitor,
        adapters,
        new LoggerProviderEventLogger(host.Services.GetRequiredService<ILogger<AIProviderManager>>()),
        providerRegistry: providerRegistry);
    var reasoningEngine = new ReasoningEngine(aiProviderClient);
    var embeddingGenerator = new AIProviderEmbeddingGenerator(aiProviderClient);

    var securityOptions = loader.Load<SecurityOptions>("Security.json");
    var policyEngine = new PolicyEngine(
        securityOptions.GlobalPolicies.Select(e => new EOS.Gates.PolicyEntry(e.ActionType, e.Verdict, e.Reason)).ToList(),
        securityOptions.ProjectPolicies.Select(e => new EOS.Gates.PolicyEntry(e.ActionType, e.Verdict, e.Reason)).ToList(),
        securityOptions.UserPolicies.Select(e => new EOS.Gates.PolicyEntry(e.ActionType, e.Verdict, e.Reason)).ToList(),
        securityOptions.RuntimePolicies.Select(e => new EOS.Gates.PolicyEntry(e.ActionType, e.Verdict, e.Reason)).ToList());
    var ruleEngine = new RuleEngine();
    var riskEngine = new RiskEngine();
    var approvalEngine = new ApprovalEngine();

    var resourceCeilings = new ResourceCeilings(
        CpuCeilingPercent: thresholdsOptions.CpuCeilingPercent,
        RamCeilingMegabytes: thresholdsOptions.RamCeilingMegabytes,
        DiskCeilingMegabytes: thresholdsOptions.DiskCeilingMegabytes,
        ModelUsageCeilingTokens: thresholdsOptions.ModelUsageCeilingTokens,
        ContextSizeCeilingTokens: thresholdsOptions.ContextSizeCeilingTokens,
        BackgroundTasksCeilingCount: thresholdsOptions.BackgroundTasksCeilingCount);
    var emergencyShutdownState = new EmergencyShutdownState();

    var protectionGate = new ProtectionGate(
        policyEngine,
        ruleEngine,
        riskEngine,
        approvalEngine,
        emergencyShutdownState,
        resourceCeilings,
        host.Services.GetRequiredService<ILogger<ProtectionGate>>());

    var connectionOptions = DataStoreConnectionOptions.FromEnvironment();
    var knowledgeGraphStore = new KnowledgeGraphStore(connectionOptions.SqlServerConnectionString);
    await knowledgeGraphStore.EnsureTableExistsAsync(CancellationToken.None);
    var rankingWeights = new RankingWeights(
        VectorSimilarity: thresholdsOptions.RankingVectorSimilarityWeight,
        Recency: thresholdsOptions.RankingRecencyWeight,
        DomainMatch: thresholdsOptions.RankingDomainMatchWeight,
        AccessFrequency: thresholdsOptions.RankingAccessFrequencyWeight);
    var eventMediator = new EventMediator();
    var redisMemoryStore = new RedisMemoryStore(connectionOptions.RedisConnectionString);
    var vectorStore = new ChromaVectorStore(connectionOptions.ChromaDbEndpoint);
    var knowledgeClient = new KnowledgeClient(
        knowledgeGraphStore,
        rankingWeights,
        vectorStore,
        new RedisMemorySourceStore(redisMemoryStore),
        new EventMediatorContextAssemblyEventPublisher(eventMediator),
        embeddingGenerator,
        new EventMediatorLessonLearnedEventPublisher(eventMediator),
        new EventMediatorMemoryConsolidatedEventPublisher(eventMediator));

    AutomaticConsolidationTriggerHandlers.RegisterSubscriptions(eventMediator, knowledgeClient);

    var askCommand = new AskCommand(
        reasoningEngine, protectionGate, knowledgeClient, host.Services.GetRequiredService<ILogger<AskCommand>>());

    return await askCommand.ExecuteAsync(text);
}
finally
{
    foreach (var httpClient in httpClients)
    {
        httpClient.Dispose();
    }
}

internal sealed class LoggerProviderEventLogger(ILogger logger) : IProviderEventLogger
{
    public void LogEvent(string message) => logger.LogInformation("{Message}", message);

    public void LogWarning(string message) => logger.LogWarning("{Message}", message);
}

internal sealed class AIProviderEmbeddingGenerator(IEmbeddingProviderClient embeddingProviderClient) : IEmbeddingGenerator
{
    public async Task<IReadOnlyList<float>> EmbedAsync(string content, CancellationToken cancellationToken = default)
    {
        var vector = await embeddingProviderClient.EmbedAsync(content, cancellationToken);
        return vector.Values;
    }
}

internal sealed record ContextAssembledPayload(Guid RequestId, int ItemCount, bool Truncated);

internal sealed class EventMediatorContextAssemblyEventPublisher(EventMediator eventMediator) : IContextAssemblyEventPublisher
{
    public void PublishContextAssembled(Guid requestId, int itemCount, bool truncated)
    {
        eventMediator.Publish(EventEnvelope<ContextAssembledPayload>.Create(
            eventType: "ContextAssembled",
            version: "v1",
            producer: "EOS.Knowledge",
            payload: new ContextAssembledPayload(requestId, itemCount, truncated)));
    }
}

internal sealed class RedisMemorySourceStore(RedisMemoryStore redisMemoryStore) : IMemorySourceStore
{
    public Task<string?> GetContentAsync(MemoryRef source, CancellationToken cancellationToken = default) =>
        redisMemoryStore.GetAsync(source.Key, cancellationToken);

    public async Task<bool> IsConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default) =>
        await redisMemoryStore.GetAsync(ConsolidatedMarkerKey(source), cancellationToken) is not null;

    public Task MarkConsolidatedAsync(MemoryRef source, CancellationToken cancellationToken = default) =>
        redisMemoryStore.SetAsync(ConsolidatedMarkerKey(source), "true", null, cancellationToken);

    private static string ConsolidatedMarkerKey(MemoryRef source) => $"{source.Key}:consolidated";
}

internal sealed record LessonLearnedPayload(Guid EpisodicEntryId, string Source);

internal sealed class EventMediatorLessonLearnedEventPublisher(EventMediator eventMediator) : ILessonLearnedEventPublisher
{
    public void PublishLessonLearned(Guid episodicEntryId, string source)
    {
        eventMediator.Publish(EventEnvelope<LessonLearnedPayload>.Create(
            eventType: "LessonLearned",
            version: "v1",
            producer: "EOS.Knowledge",
            payload: new LessonLearnedPayload(episodicEntryId, source)));
    }
}

internal sealed record MemoryConsolidatedPayload(MemoryType SourceMemoryType, Guid EpisodicEntryId);

internal sealed class EventMediatorMemoryConsolidatedEventPublisher(EventMediator eventMediator) : IMemoryConsolidatedEventPublisher
{
    public void PublishMemoryConsolidated(MemoryType sourceMemoryType, Guid episodicEntryId)
    {
        eventMediator.Publish(EventEnvelope<MemoryConsolidatedPayload>.Create(
            eventType: "MemoryConsolidated",
            version: "v1",
            producer: "EOS.Knowledge",
            payload: new MemoryConsolidatedPayload(sourceMemoryType, episodicEntryId)));
    }
}

/// <summary>
/// Memory-Management-Specification-v1.0 §16.1's "Automatic, on Gate failure (novel failure)"
/// trigger (ADR-015-003): the signal <c>Program.cs</c> subscribes to via <c>EventMediator</c>
/// to invoke <see cref="IKnowledgeClient.ConsolidateAsync"/> with
/// <c>suppressLessonLearned: true</c>, since <c>EOS.Gates</c> has already emitted
/// <c>LessonLearned</c> per Constitution §0.8.3 (ADR-015-002).
/// </summary>
public sealed record GateFailureConsolidationSignal(
    MemoryType SourceMemoryType, string SourceKey, string Reason, string[] EvidenceRefs);

/// <summary>
/// Memory-Management-Specification-v1.0 §16.1's "Automatic, on IncidentResolved" trigger
/// (ADR-015-003): the signal <c>Program.cs</c> subscribes to via <c>EventMediator</c> to invoke
/// <see cref="IKnowledgeClient.ConsolidateAsync"/> with real <c>LessonLearned</c> emission
/// (ADR-015-002).
/// </summary>
public sealed record IncidentResolvedConsolidationSignal(
    MemoryType SourceMemoryType, string SourceKey, string Reason, string[] EvidenceRefs);

/// <summary>
/// The two automatic-trigger <c>EventMediator</c> handlers (ADR-015-003), extracted to named,
/// externally-callable methods so a test can invoke the exact production logic rather than a
/// duplicated copy of it. <c>public</c> solely to make this possible from
/// <c>EOS.Runner.Tests</c> — not a new architectural layer, just the same two handler bodies
/// that were previously inline lambdas in <c>Program.cs</c>.
/// </summary>
public static class AutomaticConsolidationTriggerHandlers
{
    /// <summary>
    /// The exact registration `Program.cs` performs — extracted so a test can invoke this same
    /// registration path (mapping each signal type to its handler) rather than re-declaring its
    /// own parallel `Subscribe` calls, which would not catch a wrong or missing mapping here.
    /// </summary>
    public static void RegisterSubscriptions(EventMediator eventMediator, IKnowledgeClient knowledgeClient)
    {
        eventMediator.Subscribe<GateFailureConsolidationSignal>(
            envelope => HandleGateFailureSignal(envelope, knowledgeClient));

        eventMediator.Subscribe<IncidentResolvedConsolidationSignal>(
            envelope => HandleIncidentResolvedSignal(envelope, knowledgeClient));
    }

    public static void HandleGateFailureSignal(
        EventEnvelope<GateFailureConsolidationSignal> envelope, IKnowledgeClient knowledgeClient)
    {
        var payload = envelope.Payload;
        knowledgeClient.ConsolidateAsync(
            new MemoryRef(payload.SourceMemoryType, payload.SourceKey),
            payload.Reason,
            payload.EvidenceRefs,
            suppressLessonLearned: true).GetAwaiter().GetResult();
    }

    public static void HandleIncidentResolvedSignal(
        EventEnvelope<IncidentResolvedConsolidationSignal> envelope, IKnowledgeClient knowledgeClient)
    {
        var payload = envelope.Payload;
        knowledgeClient.ConsolidateAsync(
            new MemoryRef(payload.SourceMemoryType, payload.SourceKey),
            payload.Reason,
            payload.EvidenceRefs,
            suppressLessonLearned: false).GetAwaiter().GetResult();
    }
}
