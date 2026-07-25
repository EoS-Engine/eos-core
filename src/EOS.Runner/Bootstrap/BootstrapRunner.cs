using System.Diagnostics;
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

        runner.AddStep("Start Infrastructure", _ =>
        {
            if (storageOptions is null || string.IsNullOrWhiteSpace(storageOptions.DataDirectory))
            {
                throw new ConfigurationValidationException("StorageOptions.DataDirectory is not set.");
            }

            return Task.CompletedTask;
        });

        runner.AddStep("Health Check", _ =>
        {
            if (thresholdsOptions is null || thresholdsOptions.ResourceCriticalPercent <= thresholdsOptions.ResourceWarningPercent)
            {
                throw new ConfigurationValidationException(
                    "ThresholdsOptions.ResourceCriticalPercent must be greater than ResourceWarningPercent.");
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
