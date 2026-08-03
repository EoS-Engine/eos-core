using System.Diagnostics;
using EOS.Infrastructure;
using EOS.SharedKernel.Configuration;
using Microsoft.Extensions.Logging;

namespace EOS.Runner.Bootstrap;

public sealed class BootstrapRunner(ILogger<BootstrapRunner> logger)
{
    private readonly List<BootstrapStep> _steps = [];

    public BootstrapRunner AddStep(string name, Func<CancellationToken, Task> execute)
    {
        _steps.Add(new BootstrapStep(name, execute));
        return this;
    }

    public async Task<IReadOnlyList<BootstrapResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<BootstrapResult>();

        for (var i = 0; i < _steps.Count; i++)
        {
            var step = _steps[i];
            var startedAt = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await step.ExecuteAsync(cancellationToken);
                stopwatch.Stop();
                var result = new BootstrapResult(step.Name, true, startedAt, DateTimeOffset.UtcNow, stopwatch.Elapsed, null);
                results.Add(result);
                logger.LogInformation(
                    "[{StepNumber}/{TotalSteps}] {StepName} - Success ({DurationMs}ms)",
                    i + 1, _steps.Count, step.Name, stopwatch.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var result = new BootstrapResult(step.Name, false, startedAt, DateTimeOffset.UtcNow, stopwatch.Elapsed, ex.Message);
                results.Add(result);
                logger.LogError(
                    "[{StepNumber}/{TotalSteps}] {StepName} - Failed ({DurationMs}ms): {Error}",
                    i + 1, _steps.Count, step.Name, stopwatch.Elapsed.TotalMilliseconds, ex.Message);
                break;
            }
        }

        return results;
    }

    public static BootstrapRunner CreateEosBootstrap(IConfigurationLoader configurationLoader, ILogger<BootstrapRunner> logger)
    {
        var runner = new BootstrapRunner(logger);

        EosOptions? eosOptions = null;
        PlannerOptions? plannerOptions = null;
        InferenceOptions? inferenceOptions = null;
        ProvidersOptions? providersOptions = null;
        ThresholdsOptions? thresholdsOptions = null;
        SecurityOptions? securityOptions = null;
        DashboardOptions? dashboardOptions = null;
        KnowledgeOptions? knowledgeOptions = null;
        StorageOptions? storageOptions = null;
        FeatureFlagsOptions? featureFlagsOptions = null;
        IReadOnlyList<StoreHealthResult>? storeHealthResults = null;

        runner.AddStep("Install", _ =>
        {
            if (!Directory.Exists(configurationLoader.ConfigDirectory))
            {
                throw new ConfigurationValidationException(
                    $"Configuration directory not found: {configurationLoader.ConfigDirectory}");
            }

            return Task.CompletedTask;
        });

        runner.AddStep("Validate", _ =>
        {
            eosOptions = configurationLoader.Load<EosOptions>("EOS.json");
            plannerOptions = configurationLoader.Load<PlannerOptions>("Planner.json");
            inferenceOptions = configurationLoader.Load<InferenceOptions>("Inference.json");
            providersOptions = configurationLoader.Load<ProvidersOptions>("Providers.json");
            thresholdsOptions = configurationLoader.Load<ThresholdsOptions>("Thresholds.json");
            securityOptions = configurationLoader.Load<SecurityOptions>("Security.json");
            dashboardOptions = configurationLoader.Load<DashboardOptions>("Dashboard.json");
            knowledgeOptions = configurationLoader.Load<KnowledgeOptions>("Knowledge.json");
            storageOptions = configurationLoader.Load<StorageOptions>("Storage.json");
            featureFlagsOptions = configurationLoader.Load<FeatureFlagsOptions>("FeatureFlags.json");

            return Task.CompletedTask;
        });

        runner.AddStep("Generate Keys", _ =>
        {
            if (securityOptions is null)
            {
                throw new ConfigurationValidationException("SecurityOptions was not loaded before Generate Keys.");
            }

            return Task.CompletedTask;
        });

        runner.AddStep("Configure Providers", cancellationToken =>
        {
            if (providersOptions is null || providersOptions.Providers.Count == 0)
            {
                throw new ConfigurationValidationException("ProvidersOptions must declare at least one provider.");
            }

            foreach (var provider in providersOptions.Providers)
            {
                if (!Uri.TryCreate(provider.Endpoint, UriKind.Absolute, out Uri? _))
                {
                    throw new ConfigurationValidationException(
                        $"Provider '{provider.Name}' has an invalid endpoint: {provider.Endpoint}");
                }
            }

            return Task.CompletedTask;
        });

        runner.AddStep("Start Infrastructure", async cancellationToken =>
        {
            if (storageOptions is null || string.IsNullOrWhiteSpace(storageOptions.DataDirectory))
            {
                throw new ConfigurationValidationException("StorageOptions.DataDirectory is not set.");
            }

            var connectionOptions = DataStoreConnectionOptions.FromEnvironment();
            var healthChecker = new DataStoreHealthChecker(connectionOptions, storageOptions.DataDirectory);
            storeHealthResults = await healthChecker.CheckAllAsync(cancellationToken);

            var unhealthy = storeHealthResults.Where(r => !r.Healthy).ToList();
            if (unhealthy.Count > 0)
            {
                var details = string.Join("; ", unhealthy.Select(r => $"{r.StoreName}: {r.Error}"));
                throw new ConfigurationValidationException($"One or more data stores are unreachable: {details}");
            }
        });

        runner.AddStep("Health Check", _ =>
        {
            if (thresholdsOptions is null || thresholdsOptions.ResourceCriticalPercent <= thresholdsOptions.ResourceWarningPercent)
            {
                throw new ConfigurationValidationException(
                    "ThresholdsOptions.ResourceCriticalPercent must be greater than ResourceWarningPercent.");
            }

            // Resource-Management-Specification-v1.0 §17.5: each resource type's four tiers
            // (Safe implicitly below Warning) must be strictly ordered, or Capacity Manager's
            // classification silently misclassifies measured values instead of failing closed.
            var capacityTierTriples = new (string ResourceType, int Warning, int Critical, int Emergency)[]
            {
                ("Cpu", thresholdsOptions.CpuWarningThresholdPercent, thresholdsOptions.CpuCriticalThresholdPercent, thresholdsOptions.CpuEmergencyThresholdPercent),
                ("Ram", thresholdsOptions.RamWarningThresholdMegabytes, thresholdsOptions.RamCriticalThresholdMegabytes, thresholdsOptions.RamEmergencyThresholdMegabytes),
                ("Disk", thresholdsOptions.DiskWarningThresholdMegabytes, thresholdsOptions.DiskCriticalThresholdMegabytes, thresholdsOptions.DiskEmergencyThresholdMegabytes),
                ("ModelUsage", thresholdsOptions.ModelUsageWarningThresholdTokens, thresholdsOptions.ModelUsageCriticalThresholdTokens, thresholdsOptions.ModelUsageEmergencyThresholdTokens),
                ("QueueLength", thresholdsOptions.QueueLengthWarningThresholdCount, thresholdsOptions.QueueLengthCriticalThresholdCount, thresholdsOptions.QueueLengthEmergencyThresholdCount),
                ("BackgroundTasks", thresholdsOptions.BackgroundTasksWarningThresholdCount, thresholdsOptions.BackgroundTasksCriticalThresholdCount, thresholdsOptions.BackgroundTasksEmergencyThresholdCount),
                ("CacheUsage", thresholdsOptions.CacheUsageWarningThresholdPercent, thresholdsOptions.CacheUsageCriticalThresholdPercent, thresholdsOptions.CacheUsageEmergencyThresholdPercent),
            };

            foreach (var (resourceType, warning, critical, emergency) in capacityTierTriples)
            {
                if (warning >= critical || critical >= emergency)
                {
                    throw new ConfigurationValidationException(
                        $"ThresholdsOptions.{resourceType} tier boundaries must satisfy Warning < Critical < Emergency " +
                        $"(got Warning={warning}, Critical={critical}, Emergency={emergency}).");
                }
            }

            if (storeHealthResults is null || storeHealthResults.Any(r => !r.Healthy))
            {
                throw new ConfigurationValidationException("Not all data stores reported healthy during Start Infrastructure.");
            }

            return Task.CompletedTask;
        });

        runner.AddStep("Initialize Knowledge", _ =>
        {
            if (knowledgeOptions is null || string.IsNullOrWhiteSpace(knowledgeOptions.VectorStoreCollection))
            {
                throw new ConfigurationValidationException("KnowledgeOptions.VectorStoreCollection is not set.");
            }

            return Task.CompletedTask;
        });

        runner.AddStep("Seed Planner", _ =>
        {
            if (plannerOptions is null || plannerOptions.ReplanningCadenceMinutes <= 0)
            {
                throw new ConfigurationValidationException("PlannerOptions.ReplanningCadenceMinutes must be greater than zero.");
            }

            return Task.CompletedTask;
        });

        runner.AddStep("Run Validation", _ =>
        {
            if (eosOptions is null || plannerOptions is null || inferenceOptions is null || providersOptions is null ||
                thresholdsOptions is null || securityOptions is null || dashboardOptions is null || knowledgeOptions is null ||
                storageOptions is null || featureFlagsOptions is null)
            {
                throw new ConfigurationValidationException("Not all configuration options were successfully loaded before Run Validation.");
            }

            return Task.CompletedTask;
        });

        runner.AddStep("Ready", _ => Task.CompletedTask);

        return runner;
    }
}
