using EOS.AIProvider;
using EOS.Gates;
using EOS.Infrastructure;
using EOS.Knowledge;
using EOS.KnowledgeGraph;
using EOS.Reasoning;
using EOS.Runner.Bootstrap;
using EOS.Runner.Commands;
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
var ollamaEndpoint = providersOptions.Providers.Single(p => p.Name == "ollama").Endpoint;

using var httpClient = new HttpClient { BaseAddress = new Uri(ollamaEndpoint) };
var aiProviderClient = new OllamaProviderAdapter(
    httpClient, inferenceOptions.DefaultModel, inferenceOptions.MaxTokens, inferenceOptions.Temperature);
var reasoningEngine = new ReasoningEngine(aiProviderClient);

var protectionGate = new ProtectionGate(host.Services.GetRequiredService<ILogger<ProtectionGate>>());

var connectionOptions = DataStoreConnectionOptions.FromEnvironment();
var knowledgeGraphStore = new KnowledgeGraphStore(connectionOptions.SqlServerConnectionString);
await knowledgeGraphStore.EnsureTableExistsAsync(CancellationToken.None);
var knowledgeClient = new KnowledgeClient(knowledgeGraphStore);

var askCommand = new AskCommand(
    reasoningEngine, protectionGate, knowledgeClient, host.Services.GetRequiredService<ILogger<AskCommand>>());

return await askCommand.ExecuteAsync(text);
