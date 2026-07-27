using EOS.AIProvider;
using EOS.Gates;
using EOS.Infrastructure;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using EOS.Reasoning;
using EOS.Runner.Bootstrap;
using EOS.Runner.Commands;
using EOS.SDK;
using EOS.SharedKernel.Configuration;
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

    var securityOptions = loader.Load<SecurityOptions>("Security.json");
    var policyEngine = new PolicyEngine(
        securityOptions.GlobalPolicies.Select(e => new EOS.Gates.PolicyEntry(e.ActionType, e.Verdict, e.Reason)).ToList(),
        securityOptions.ProjectPolicies.Select(e => new EOS.Gates.PolicyEntry(e.ActionType, e.Verdict, e.Reason)).ToList(),
        securityOptions.UserPolicies.Select(e => new EOS.Gates.PolicyEntry(e.ActionType, e.Verdict, e.Reason)).ToList(),
        securityOptions.RuntimePolicies.Select(e => new EOS.Gates.PolicyEntry(e.ActionType, e.Verdict, e.Reason)).ToList());
    var ruleEngine = new RuleEngine();
    var riskEngine = new RiskEngine();
    var approvalEngine = new ApprovalEngine();

    var protectionGate = new ProtectionGate(
        policyEngine,
        ruleEngine,
        riskEngine,
        approvalEngine,
        host.Services.GetRequiredService<ILogger<ProtectionGate>>());

    var connectionOptions = DataStoreConnectionOptions.FromEnvironment();
    var knowledgeGraphStore = new KnowledgeGraphStore(connectionOptions.SqlServerConnectionString);
    await knowledgeGraphStore.EnsureTableExistsAsync(CancellationToken.None);
    var knowledgeClient = new KnowledgeClient(knowledgeGraphStore);

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
