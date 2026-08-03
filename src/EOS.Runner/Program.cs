using EOS.AIProvider;
using EOS.Gates;
using EOS.Infrastructure;
using EOS.Contracts;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using EOS.Orchestrator;
using EOS.Reasoning;
using EOS.Resources;
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

if (args is not ["ask", _] and not ["compress"])
{
    return 0;
}

var providersOptions = loader.Load<ProvidersOptions>("Providers.json");
var inferenceOptions = loader.Load<InferenceOptions>("Inference.json");
var thresholdsOptions = loader.Load<ThresholdsOptions>("Thresholds.json");
var knowledgeOptions = loader.Load<KnowledgeOptions>("Knowledge.json");

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

    var eventMediator = new EventMediator();

    // WP-021: Resource Management (Resource-Management-Specification-v1.0 §10.2/§10.3/§17/§18).
    var resourceMonitor = new ResourceMonitor(
        thresholdsOptions.ResourceSamplingIntervalSeconds,
        thresholdsOptions.ModelIdleResidencyTimeoutSeconds,
        new EventMediatorModelLoadedEventPublisher(eventMediator),
        new EventMediatorModelUnloadedEventPublisher(eventMediator));
    var capacityThresholds = new CapacityThresholds(
        Cpu: new ResourceTierBoundaries(
            thresholdsOptions.CpuWarningThresholdPercent, thresholdsOptions.CpuCriticalThresholdPercent, thresholdsOptions.CpuEmergencyThresholdPercent),
        Ram: new ResourceTierBoundaries(
            thresholdsOptions.RamWarningThresholdMegabytes, thresholdsOptions.RamCriticalThresholdMegabytes, thresholdsOptions.RamEmergencyThresholdMegabytes),
        Disk: new ResourceTierBoundaries(
            thresholdsOptions.DiskWarningThresholdMegabytes, thresholdsOptions.DiskCriticalThresholdMegabytes, thresholdsOptions.DiskEmergencyThresholdMegabytes),
        ModelUsage: new ResourceTierBoundaries(
            thresholdsOptions.ModelUsageWarningThresholdTokens, thresholdsOptions.ModelUsageCriticalThresholdTokens, thresholdsOptions.ModelUsageEmergencyThresholdTokens),
        QueueLength: new ResourceTierBoundaries(
            thresholdsOptions.QueueLengthWarningThresholdCount, thresholdsOptions.QueueLengthCriticalThresholdCount, thresholdsOptions.QueueLengthEmergencyThresholdCount),
        BackgroundTasks: new ResourceTierBoundaries(
            thresholdsOptions.BackgroundTasksWarningThresholdCount, thresholdsOptions.BackgroundTasksCriticalThresholdCount, thresholdsOptions.BackgroundTasksEmergencyThresholdCount),
        CacheUsage: new ResourceTierBoundaries(
            thresholdsOptions.CacheUsageWarningThresholdPercent, thresholdsOptions.CacheUsageCriticalThresholdPercent, thresholdsOptions.CacheUsageEmergencyThresholdPercent));
    var capacityManager = new CapacityManager(
        capacityThresholds,
        new EventMediatorResourceThresholdCrossedEventPublisher(eventMediator),
        new EventMediatorEmergencyCapacitySignalEventPublisher(eventMediator),
        new EventMediatorResourceRecoveredEventPublisher(eventMediator));

    // WP-022: Resource Management Quotas/Background Task Controller/Model Residency
    // (Resource-Management-Specification-v1.0 §10.4/§10.6/§14/§16/§19).
    var resourceClassQuotas = new ResourceClassQuotas(
        UserRequests: new ResourceClassQuota(thresholdsOptions.CpuQuotaUserRequestsPercent, thresholdsOptions.RamQuotaUserRequestsMegabytes, thresholdsOptions.ModelSlotQuotaUserRequestsCount),
        InteractiveSessions: new ResourceClassQuota(thresholdsOptions.CpuQuotaInteractiveSessionsPercent, thresholdsOptions.RamQuotaInteractiveSessionsMegabytes, thresholdsOptions.ModelSlotQuotaInteractiveSessionsCount),
        AutonomousTasks: new ResourceClassQuota(thresholdsOptions.CpuQuotaAutonomousTasksPercent, thresholdsOptions.RamQuotaAutonomousTasksMegabytes, thresholdsOptions.ModelSlotQuotaAutonomousTasksCount),
        BackgroundMaintenance: new ResourceClassQuota(thresholdsOptions.CpuQuotaBackgroundMaintenancePercent, thresholdsOptions.RamQuotaBackgroundMaintenanceMegabytes, thresholdsOptions.ModelSlotQuotaBackgroundMaintenanceCount),
        LearningActivities: new ResourceClassQuota(thresholdsOptions.CpuQuotaLearningActivitiesPercent, thresholdsOptions.RamQuotaLearningActivitiesMegabytes, thresholdsOptions.ModelSlotQuotaLearningActivitiesCount));
    var quotaManager = new QuotaManager(
        resourceClassQuotas,
        thresholdsOptions.QuotaStarvationDenialCountThreshold,
        thresholdsOptions.QuotaWindowSeconds,
        new EventMediatorResourceQuotaExhaustedEventPublisher(eventMediator));
    var backgroundTaskController = new BackgroundTaskController(
        () => capacityManager.ComputeTier(ResourceType.Cpu, resourceMonitor.Sample(ResourceType.Cpu)),
        quotaManager,
        new EventMediatorBackgroundJobGrantedEventPublisher(eventMediator),
        new EventMediatorBackgroundJobDeferredEventPublisher(eventMediator));

    var resourceManagementClient = new ResourceManagementClient(resourceMonitor, capacityManager, backgroundTaskController);

    // §20.1 Consumed Events: TaskStarted/TaskCompleted/TaskBlocked inform Queue Length/
    // Background Task observation; InferenceRouted/InferenceCompleted inform Model Usage
    // (WP-021 Implementation Plan Decision D6). Structurally ready — no real producer exists
    // yet anywhere in this repository (no Scheduler, no real AI Provider event publishing),
    // mirroring AutomaticConsolidationTriggerHandlers' own subscription pattern.
    eventMediator.Subscribe<TaskStartedPayload>(envelope => resourceMonitor.RecordTaskStarted(envelope.Payload.TaskId));
    eventMediator.Subscribe<TaskCompletedPayload>(envelope => resourceMonitor.RecordTaskCompleted(envelope.Payload.TaskId));
    eventMediator.Subscribe<TaskBlockedPayload>(envelope => resourceMonitor.RecordTaskBlocked(envelope.Payload.TaskId));
    eventMediator.Subscribe<InferenceRoutedPayload>(envelope => resourceMonitor.RecordInferenceRouted(envelope.Payload.Model));
    eventMediator.Subscribe<InferenceCompletedPayload>(envelope => resourceMonitor.RecordInferenceCompleted(envelope.Payload.Model));

    var protectionGate = new ProtectionGate(
        policyEngine,
        ruleEngine,
        riskEngine,
        approvalEngine,
        emergencyShutdownState,
        resourceCeilings,
        resourceManagementClient,
        host.Services.GetRequiredService<ILogger<ProtectionGate>>());

    var connectionOptions = DataStoreConnectionOptions.FromEnvironment();
    var knowledgeGraphStore = new KnowledgeGraphStore(connectionOptions.SqlServerConnectionString);
    await knowledgeGraphStore.EnsureTableExistsAsync(CancellationToken.None);
    var rankingWeights = new RankingWeights(
        VectorSimilarity: thresholdsOptions.RankingVectorSimilarityWeight,
        Recency: thresholdsOptions.RankingRecencyWeight,
        DomainMatch: thresholdsOptions.RankingDomainMatchWeight,
        AccessFrequency: thresholdsOptions.RankingAccessFrequencyWeight);
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

    var reasoningEngine = new ReasoningEngine(
        aiProviderClient,
        new KnowledgeContextAcquisitionProvider(knowledgeClient),
        new ReasoningEngineOptions(
            ContextExpansionCap: thresholdsOptions.ReasoningContextExpansionCap,
            LowConfidenceFloor: thresholdsOptions.ReasoningLowConfidenceFloor),
        new EventMediatorDecisionMadeEventPublisher(eventMediator),
        new EventMediatorLowConfidenceDecisionFlaggedEventPublisher(eventMediator),
        new EventMediatorContextExpansionRequestedEventPublisher(eventMediator),
        host.Services.GetRequiredService<ILogger<ReasoningEngine>>());

    // Real, independently tested infrastructure with no production caller yet — no WP before
    // this one has a reason to classify a node or add a relationship in the "ask" path.
    var freshnessTypeWeights = knowledgeOptions.FreshnessTypeWeights
        .ToDictionary(pair => Enum.Parse<TaxonomyClassification>(pair.Key), pair => pair.Value);
    var knowledgeManagementClient = new KnowledgeManagementClient(
        knowledgeGraphStore,
        knowledgeClient,
        new OntologyValidator(
            knowledgeGraphStore,
            knowledgeOptions.DependsOnDisallowedTargetTypes.Select(Enum.Parse<KnowledgeNodeType>).ToList(),
            knowledgeOptions.GovernanceApprovalRequiredRelationshipTypes.Select(Enum.Parse<RelationshipType>).ToList()),
        new FreshnessCalculator(knowledgeOptions.FreshnessDecayHalfLifeDays, freshnessTypeWeights),
        new DuplicateDetector(knowledgeGraphStore, new StructuralOnlyCompareProviderStub()),
        protectionGate,
        new KnowledgeRankingWeights(
            knowledgeOptions.RankingConfidenceWeight,
            knowledgeOptions.RankingReliabilityWeight,
            knowledgeOptions.RankingRelationshipRelevanceWeight,
            knowledgeOptions.RankingDeprecationPenaltyWeight),
        knowledgeOptions.FreshnessExpirationThreshold,
        new EventMediatorKnowledgeClassifiedEventPublisher(eventMediator),
        new EventMediatorKnowledgeRelationshipAddedEventPublisher(eventMediator),
        new EventMediatorKnowledgeQualityUpdatedEventPublisher(eventMediator),
        new EventMediatorKnowledgeGovernanceActionRequestedEventPublisher(eventMediator),
        new EventMediatorKnowledgeGovernanceActionAppliedEventPublisher(eventMediator),
        new EventMediatorKnowledgeFreshnessExpiredEventPublisher(eventMediator),
        new EventMediatorKnowledgeDuplicateFlaggedEventPublisher(eventMediator));
    _ = knowledgeManagementClient;

    var archivedContentStore = new ArchivedContentStore(connectionOptions.SqlServerConnectionString);
    await archivedContentStore.EnsureTableExistsAsync(CancellationToken.None);
    var compressionSweep = new CompressionSweep(
        knowledgeGraphStore,
        archivedContentStore,
        new NotYetPromotedPipelineStageStore(),
        new NeverReadRecentlyStub(),
        new NoActiveRetentionHoldsStub(),
        new ReasoningEngineSummarizerAdapter(reasoningEngine, thresholdsOptions.SummarizationStubTruncationLength),
        new ResourceManagementBackgroundSlotRequester(resourceManagementClient, eventMediator),
        new EventMediatorMemoryCompressedEventPublisher(eventMediator));
    // Real, independently tested infrastructure with no production caller yet (mirrors
    // RedisMemoryStore's own WP-014 precedent) — no WP before this one writes production
    // Short-term/Session data, so there is nothing yet to pass this policy's computed TTL to.
    var memoryExpirationPolicy = new MemoryExpirationPolicy(
        thresholdsOptions.ShortTermMemoryExpirationSeconds, thresholdsOptions.SessionMemoryIdleTimeoutSeconds);
    _ = memoryExpirationPolicy;

    if (args is ["compress"])
    {
        var compressionLogger = host.Services.GetRequiredService<ILogger<CompressionSweep>>();
        var compressionActionRequest = new ActionRequest(
            ActionId: Guid.NewGuid(), ActionType: "MemoryCompression", Actor: "HumanOperator", RiskScore: 10);
        var compressionValidationResult = protectionGate.Validate(compressionActionRequest);
        if (compressionValidationResult.Verdict != ProtectionVerdict.Allow)
        {
            compressionLogger.LogError(
                "Compression sweep was not allowed: {Verdict} - {Reason}",
                compressionValidationResult.Verdict, compressionValidationResult.Reason);
            return 1;
        }

        var compressedCount = await compressionSweep.RunAsync();
        Console.WriteLine($"Compression sweep complete: {compressedCount} entr{(compressedCount == 1 ? "y" : "ies")} compressed.");
        return 0;
    }

    var askCommand = new AskCommand(
        reasoningEngine, protectionGate, knowledgeClient, host.Services.GetRequiredService<ILogger<AskCommand>>());

    return await askCommand.ExecuteAsync(args[1]);
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

/// <summary>
/// WP-019 Slice 3's Composition Root Adapter (<see cref="IContextAcquisitionProvider"/>,
/// Implementation Plan Revision 3 Area 1): translates <see cref="ReasoningContextScope"/> to
/// <see cref="ContextRequest"/> and calls <see cref="IKnowledgeClient.AssembleContextAsync"/> —
/// the only legal path, since <c>EOS.Reasoning</c> itself cannot reference <c>EOS.Knowledge</c>
/// (Constitution Part 1 §1.2). <c>ProjectScope</c> carries <see cref="ReasoningContextScope.ProjectScope"/>
/// when supplied, else <see cref="ReasoningContextScope.DomainTags"/> — §13.2 names both fields,
/// but <see cref="ContextRequest"/>'s current shape has one project-scope filter slot for them.
/// <see cref="ReasoningEngine.DefaultContextBudget"/> is reused here (rather than a second,
/// independent literal) so the initial-acquisition default and the Context Expansion doubling
/// base (§12.4) stay in sync by construction.
/// </summary>
internal sealed class KnowledgeContextAcquisitionProvider(IKnowledgeClient knowledgeClient) : IContextAcquisitionProvider
{
    public async Task<AcquiredContext> AcquireContextAsync(
        ReasoningContextScope scope, CancellationToken cancellationToken = default)
    {
        var contextRequest = new ContextRequest(
            TokenOrSizeBudget: scope.Budget ?? ReasoningEngine.DefaultContextBudget,
            IncludesWorking: false,
            IncludesShortTerm: false,
            IncludesEpisodic: true,
            IncludesSemantic: true,
            ProjectScope: scope.ProjectScope ?? scope.DomainTags,
            Filters: null,
            TaskId: null);

        var payload = await knowledgeClient.AssembleContextAsync(contextRequest, cancellationToken);

        return new AcquiredContext(
            payload.Items.Select(item => item.Content).ToList(),
            payload.Truncated);
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

internal sealed record DecisionMadePayload(Guid DecisionId, Guid RequestId, double Confidence, double RiskScore, ReasoningType ReasoningTypeApplied);

internal sealed class EventMediatorDecisionMadeEventPublisher(EventMediator eventMediator) : IDecisionMadeEventPublisher
{
    public void PublishDecisionMade(Guid decisionId, Guid requestId, double confidence, double riskScore, ReasoningType reasoningTypeApplied)
    {
        eventMediator.Publish(EventEnvelope<DecisionMadePayload>.Create(
            eventType: "DecisionMade",
            version: "v1",
            producer: "EOS.Reasoning",
            payload: new DecisionMadePayload(decisionId, requestId, confidence, riskScore, reasoningTypeApplied),
            correlationId: requestId));
    }
}

internal sealed record LowConfidenceDecisionFlaggedPayload(Guid DecisionId, double Confidence, double Threshold);

internal sealed class EventMediatorLowConfidenceDecisionFlaggedEventPublisher(EventMediator eventMediator) : ILowConfidenceDecisionFlaggedEventPublisher
{
    public void PublishLowConfidenceDecisionFlagged(Guid decisionId, Guid correlationId, double confidence, double threshold)
    {
        eventMediator.Publish(EventEnvelope<LowConfidenceDecisionFlaggedPayload>.Create(
            eventType: "LowConfidenceDecisionFlagged",
            version: "v1",
            producer: "EOS.Reasoning",
            payload: new LowConfidenceDecisionFlaggedPayload(decisionId, confidence, threshold),
            correlationId: correlationId));
    }
}

internal sealed record ContextExpansionRequestedPayload(Guid RequestId, ReasoningContextScope OriginalScope, ReasoningContextScope ExpandedScope);

internal sealed class EventMediatorContextExpansionRequestedEventPublisher(EventMediator eventMediator) : IContextExpansionRequestedEventPublisher
{
    public void PublishContextExpansionRequested(Guid requestId, ReasoningContextScope originalScope, ReasoningContextScope expandedScope)
    {
        eventMediator.Publish(EventEnvelope<ContextExpansionRequestedPayload>.Create(
            eventType: "ContextExpansionRequested",
            version: "v1",
            producer: "EOS.Reasoning",
            payload: new ContextExpansionRequestedPayload(requestId, originalScope, expandedScope),
            correlationId: requestId));
    }
}

internal sealed record ResourceThresholdCrossedPayload(ResourceType ResourceType, CapacityTier Tier);

internal sealed class EventMediatorResourceThresholdCrossedEventPublisher(EventMediator eventMediator) : IResourceThresholdCrossedEventPublisher
{
    public void PublishResourceThresholdCrossed(ResourceType resourceType, CapacityTier tier)
    {
        eventMediator.Publish(EventEnvelope<ResourceThresholdCrossedPayload>.Create(
            eventType: "ResourceThresholdCrossed",
            version: "v1",
            producer: "EOS.Resources",
            payload: new ResourceThresholdCrossedPayload(resourceType, tier)));
    }
}

// WP-022: Resource-Management-Specification-v1.0 §20's remaining 6 new events (all producers
// already named by §20's own table; adapters follow the exact Composition Root Adapter Pattern
// (ADR-015-001) established above for ResourceThresholdCrossed, WP-021).
internal sealed record BackgroundJobGrantedPayload(string JobId, ResourceClass ResourceClass);

internal sealed class EventMediatorBackgroundJobGrantedEventPublisher(EventMediator eventMediator) : IBackgroundJobGrantedEventPublisher
{
    public void PublishBackgroundJobGranted(string jobId, ResourceClass resourceClass)
    {
        eventMediator.Publish(EventEnvelope<BackgroundJobGrantedPayload>.Create(
            eventType: "BackgroundJobGranted",
            version: "v1",
            producer: "EOS.Resources",
            payload: new BackgroundJobGrantedPayload(jobId, resourceClass)));
    }
}

internal sealed record BackgroundJobDeferredPayload(string JobId, string Reason);

internal sealed class EventMediatorBackgroundJobDeferredEventPublisher(EventMediator eventMediator) : IBackgroundJobDeferredEventPublisher
{
    public void PublishBackgroundJobDeferred(string jobId, string reason)
    {
        eventMediator.Publish(EventEnvelope<BackgroundJobDeferredPayload>.Create(
            eventType: "BackgroundJobDeferred",
            version: "v1",
            producer: "EOS.Resources",
            payload: new BackgroundJobDeferredPayload(jobId, reason)));
    }
}

internal sealed record ModelLoadedPayload(string ModelId, double RamFootprint);

internal sealed class EventMediatorModelLoadedEventPublisher(EventMediator eventMediator) : IModelLoadedEventPublisher
{
    public void PublishModelLoaded(string modelId, double ramFootprintMegabytes)
    {
        eventMediator.Publish(EventEnvelope<ModelLoadedPayload>.Create(
            eventType: "ModelLoaded",
            version: "v1",
            producer: "EOS.Resources",
            payload: new ModelLoadedPayload(modelId, ramFootprintMegabytes)));
    }
}

internal sealed record ModelUnloadedPayload(string ModelId, double RamFootprint);

internal sealed class EventMediatorModelUnloadedEventPublisher(EventMediator eventMediator) : IModelUnloadedEventPublisher
{
    public void PublishModelUnloaded(string modelId, double ramFootprintMegabytes)
    {
        eventMediator.Publish(EventEnvelope<ModelUnloadedPayload>.Create(
            eventType: "ModelUnloaded",
            version: "v1",
            producer: "EOS.Resources",
            payload: new ModelUnloadedPayload(modelId, ramFootprintMegabytes)));
    }
}

internal sealed record ResourceQuotaExhaustedPayload(ResourceClass ResourceClass, ResourceType ResourceType);

internal sealed class EventMediatorResourceQuotaExhaustedEventPublisher(EventMediator eventMediator) : IResourceQuotaExhaustedEventPublisher
{
    public void PublishResourceQuotaExhausted(ResourceClass resourceClass, ResourceType resourceType)
    {
        eventMediator.Publish(EventEnvelope<ResourceQuotaExhaustedPayload>.Create(
            eventType: "ResourceQuotaExhausted",
            version: "v1",
            producer: "EOS.Resources",
            payload: new ResourceQuotaExhaustedPayload(resourceClass, resourceType)));
    }
}

internal sealed record EmergencyCapacitySignalPayload(ResourceType ResourceType, double MeasuredValue);

internal sealed class EventMediatorEmergencyCapacitySignalEventPublisher(EventMediator eventMediator) : IEmergencyCapacitySignalEventPublisher
{
    public void PublishEmergencyCapacitySignal(ResourceType resourceType, double measuredValue)
    {
        eventMediator.Publish(EventEnvelope<EmergencyCapacitySignalPayload>.Create(
            eventType: "EmergencyCapacitySignal",
            version: "v1",
            producer: "EOS.Resources",
            payload: new EmergencyCapacitySignalPayload(resourceType, measuredValue)));
    }
}

internal sealed record ResourceRecoveredPayload(ResourceType ResourceType);

internal sealed class EventMediatorResourceRecoveredEventPublisher(EventMediator eventMediator) : IResourceRecoveredEventPublisher
{
    public void PublishResourceRecovered(ResourceType resourceType)
    {
        eventMediator.Publish(EventEnvelope<ResourceRecoveredPayload>.Create(
            eventType: "ResourceRecovered",
            version: "v1",
            producer: "EOS.Resources",
            payload: new ResourceRecoveredPayload(resourceType)));
    }
}

// §20.1 Consumed Events (WP-021 Implementation Plan Decision D6): Constitution Part 3's
// TaskStarted/TaskCompleted/TaskBlocked payload fields (task_id, actor_role/started_at,
// evidence_refs[], blocking_gate/reason) — only task_id is needed by ResourceMonitor's
// counting logic, so only that field is modeled here. No real producer (Scheduler) exists yet
// anywhere in this repository — these subscriptions are structurally ready, not yet exercised.
internal sealed record TaskStartedPayload(Guid TaskId);

internal sealed record TaskCompletedPayload(Guid TaskId);

internal sealed record TaskBlockedPayload(Guid TaskId);

// AI-Provider-Layer-Specification-v1.0 §19's InferenceRouted/InferenceCompleted — only the
// model identifier is needed here. AIProviderManager.cs currently logs these as messages, not
// real published EventEnvelope-based events — this subscription is structurally ready, awaiting
// a future WP's real Event Catalog wiring for the AI Provider Layer.
internal sealed record InferenceRoutedPayload(string Model);

internal sealed record InferenceCompletedPayload(string Model);

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

/// <summary>
/// WP-016's stub for <see cref="IPipelineStageStore"/> (see that interface's own documentation
/// for why): no <c>PipelineRecord</c> exists anywhere until WP-026, so no entry can be proven
/// to have reached <c>Pattern</c> stage — always reporting <see langword="false"/> is the
/// architecturally correct answer, not a placeholder.
/// </summary>
internal sealed class NotYetPromotedPipelineStageStore : IPipelineStageStore
{
    public Task<bool> HasReachedPatternStageOrBeyondAsync(
        Guid episodicEntryId, CancellationToken cancellationToken = default) => Task.FromResult(false);
}

/// <summary>
/// WP-020: real backing for <see cref="ISummarizer"/> (WP-016's own interface — see its
/// documentation), bridging to <see cref="IReasoningEngineClient.SummarizeAsync"/>. Replaces
/// WP-016's <c>TruncatingSummarizerStub</c>, per the WP-020 roadmap row's own expected
/// deliverable: "WP-016's Compression sweep now calls a real <c>summarize()</c>."
/// </summary>
internal sealed class ReasoningEngineSummarizerAdapter(IReasoningEngineClient reasoningEngine, int sizeBudget) : ISummarizer
{
    public async Task<string> SummarizeAsync(string content, CancellationToken cancellationToken = default)
    {
        var summary = await reasoningEngine.SummarizeAsync(content, sizeBudget, cancellationToken);
        return summary.Content;
    }
}

/// <summary>
/// WP-016's stub for <see cref="IReadRecencyTracker"/> (see that interface's own documentation
/// for why): no read-access-tracking mechanism exists anywhere in this codebase, so always
/// reporting "not read recently" is the permissive default that never blocks eligibility on
/// data nothing here can currently supply.
/// </summary>
internal sealed class NeverReadRecentlyStub : IReadRecencyTracker
{
    public Task<bool> WasReadRecentlyAsync(Guid episodicEntryId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

/// <summary>
/// WP-016's stub for <see cref="IRetentionHoldPolicy"/> (see that interface's own documentation
/// for why): no policy source exists anywhere in this codebase to set a retention hold, so
/// always reporting "no active hold" is the correct default.
/// </summary>
internal sealed class NoActiveRetentionHoldsStub : IRetentionHoldPolicy
{
    public Task<bool> HasActiveHoldAsync(Guid episodicEntryId, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

internal sealed record MemoryCompressedPayload(Guid EntryId, int OriginalSize, int SummarySize);

/// <summary>
/// WP-022: real backing for <see cref="IBackgroundSlotRequester"/> (see that interface's own
/// documentation for why). <see cref="IResourceManagementClient.RequestBackgroundSlot"/> is
/// <c>void</c> (§21.1) — this adapter subscribes once, permanently, to <c>BackgroundJobGranted</c>/
/// <c>BackgroundJobDeferred</c> (mirroring every other permanent <see cref="EventMediator"/>
/// subscription in this composition root) and correlates the outcome by <c>job_id</c>. Because
/// <see cref="EventMediator.Publish{TPayload}"/> is synchronous and in-process, the correlating
/// handler has already run by the time <c>RequestBackgroundSlot</c> returns control here.
/// </summary>
internal sealed class ResourceManagementBackgroundSlotRequester : IBackgroundSlotRequester
{
    private readonly IResourceManagementClient _resourceManagementClient;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, bool> _outcomesByJobId = [];

    public ResourceManagementBackgroundSlotRequester(IResourceManagementClient resourceManagementClient, EventMediator eventMediator)
    {
        _resourceManagementClient = resourceManagementClient;
        eventMediator.Subscribe<BackgroundJobGrantedPayload>(envelope => RecordOutcome(envelope.Payload.JobId, granted: true));
        eventMediator.Subscribe<BackgroundJobDeferredPayload>(envelope => RecordOutcome(envelope.Payload.JobId, granted: false));
    }

    public bool RequestSlot(string jobId, ResourceClass resourceClass)
    {
        _resourceManagementClient.RequestBackgroundSlot(jobId, resourceClass);

        lock (_lock)
        {
            var granted = _outcomesByJobId.TryGetValue(jobId, out var outcome) && outcome;
            _outcomesByJobId.Remove(jobId);
            return granted;
        }
    }

    private void RecordOutcome(string jobId, bool granted)
    {
        lock (_lock)
        {
            _outcomesByJobId[jobId] = granted;
        }
    }
}

internal sealed class EventMediatorMemoryCompressedEventPublisher(EventMediator eventMediator) : IMemoryCompressedEventPublisher
{
    public void PublishMemoryCompressed(Guid entryId, int originalSize, int summarySize)
    {
        eventMediator.Publish(EventEnvelope<MemoryCompressedPayload>.Create(
            eventType: "MemoryCompressed",
            version: "v1",
            producer: "EOS.Knowledge",
            payload: new MemoryCompressedPayload(entryId, originalSize, summarySize)));
    }
}

internal sealed record KnowledgeClassifiedPayload(Guid NodeId, TaxonomyClassification TaxonomyType);

internal sealed class EventMediatorKnowledgeClassifiedEventPublisher(EventMediator eventMediator) : IKnowledgeClassifiedEventPublisher
{
    public void PublishKnowledgeClassified(Guid nodeId, TaxonomyClassification taxonomyType)
    {
        eventMediator.Publish(EventEnvelope<KnowledgeClassifiedPayload>.Create(
            eventType: "KnowledgeClassified",
            version: "v1",
            producer: "EOS.Knowledge",
            payload: new KnowledgeClassifiedPayload(nodeId, taxonomyType)));
    }
}

internal sealed record KnowledgeRelationshipAddedPayload(Guid SourceNodeId, Guid TargetNodeId, RelationshipType RelationshipType);

internal sealed class EventMediatorKnowledgeRelationshipAddedEventPublisher(EventMediator eventMediator) : IKnowledgeRelationshipAddedEventPublisher
{
    public void PublishKnowledgeRelationshipAdded(Guid sourceNodeId, Guid targetNodeId, RelationshipType relationshipType)
    {
        eventMediator.Publish(EventEnvelope<KnowledgeRelationshipAddedPayload>.Create(
            eventType: "KnowledgeRelationshipAdded",
            version: "v1",
            producer: "EOS.Knowledge",
            payload: new KnowledgeRelationshipAddedPayload(sourceNodeId, targetNodeId, relationshipType)));
    }
}

/// <summary>
/// WP-018's stub for <see cref="ICompareProvider"/> (see that interface's own documentation for
/// why): <c>IReasoningEngineClient</c> has no <c>compare()</c> member until WP-020. Always
/// reports "not semantically similar" — <see cref="DuplicateDetector"/>'s structural gate (§18.3)
/// is the real, tested signal; this stub never claims a semantic judgment it cannot make.
/// </summary>
internal sealed class StructuralOnlyCompareProviderStub : ICompareProvider
{
    public Task<bool> AreSimilarAsync(string contentA, string contentB, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

internal sealed record KnowledgeQualityUpdatedPayload(Guid NodeId, QualityProfile QualityProfile);

internal sealed class EventMediatorKnowledgeQualityUpdatedEventPublisher(EventMediator eventMediator) : IKnowledgeQualityUpdatedEventPublisher
{
    public void PublishKnowledgeQualityUpdated(Guid nodeId, QualityProfile qualityProfile)
    {
        eventMediator.Publish(EventEnvelope<KnowledgeQualityUpdatedPayload>.Create(
            eventType: "KnowledgeQualityUpdated",
            version: "v1",
            producer: "EOS.Knowledge",
            payload: new KnowledgeQualityUpdatedPayload(nodeId, qualityProfile)));
    }
}

internal sealed record KnowledgeGovernanceActionRequestedPayload(Guid NodeId, GovernanceActionType ActionType, string RequestedBy);

internal sealed class EventMediatorKnowledgeGovernanceActionRequestedEventPublisher(EventMediator eventMediator) : IKnowledgeGovernanceActionRequestedEventPublisher
{
    public void PublishKnowledgeGovernanceActionRequested(Guid nodeId, GovernanceActionType actionType, string requestedBy)
    {
        eventMediator.Publish(EventEnvelope<KnowledgeGovernanceActionRequestedPayload>.Create(
            eventType: "KnowledgeGovernanceActionRequested",
            version: "v1",
            producer: "EOS.Knowledge",
            payload: new KnowledgeGovernanceActionRequestedPayload(nodeId, actionType, requestedBy)));
    }
}

internal sealed record KnowledgeGovernanceActionAppliedPayload(Guid NodeId, GovernanceActionType ActionType, int NewVersion);

internal sealed class EventMediatorKnowledgeGovernanceActionAppliedEventPublisher(EventMediator eventMediator) : IKnowledgeGovernanceActionAppliedEventPublisher
{
    public void PublishKnowledgeGovernanceActionApplied(Guid nodeId, GovernanceActionType actionType, int newVersion)
    {
        eventMediator.Publish(EventEnvelope<KnowledgeGovernanceActionAppliedPayload>.Create(
            eventType: "KnowledgeGovernanceActionApplied",
            version: "v1",
            producer: "EOS.Knowledge",
            payload: new KnowledgeGovernanceActionAppliedPayload(nodeId, actionType, newVersion)));
    }
}

internal sealed record KnowledgeFreshnessExpiredPayload(Guid NodeId, double FreshnessScore);

internal sealed class EventMediatorKnowledgeFreshnessExpiredEventPublisher(EventMediator eventMediator) : IKnowledgeFreshnessExpiredEventPublisher
{
    public void PublishKnowledgeFreshnessExpired(Guid nodeId, double freshnessScore)
    {
        eventMediator.Publish(EventEnvelope<KnowledgeFreshnessExpiredPayload>.Create(
            eventType: "KnowledgeFreshnessExpired",
            version: "v1",
            producer: "EOS.Knowledge",
            payload: new KnowledgeFreshnessExpiredPayload(nodeId, freshnessScore)));
    }
}

internal sealed record KnowledgeDuplicateFlaggedPayload(Guid NodeIdA, Guid NodeIdB, string SimilaritySource);

internal sealed class EventMediatorKnowledgeDuplicateFlaggedEventPublisher(EventMediator eventMediator) : IKnowledgeDuplicateFlaggedEventPublisher
{
    public void PublishKnowledgeDuplicateFlagged(Guid nodeIdA, Guid nodeIdB, string similaritySource)
    {
        eventMediator.Publish(EventEnvelope<KnowledgeDuplicateFlaggedPayload>.Create(
            eventType: "KnowledgeDuplicateFlagged",
            version: "v1",
            producer: "EOS.Knowledge",
            payload: new KnowledgeDuplicateFlaggedPayload(nodeIdA, nodeIdB, similaritySource)));
    }
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
