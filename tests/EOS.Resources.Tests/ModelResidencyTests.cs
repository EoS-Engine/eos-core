using EOS.Contracts;
using EOS.Resources;

namespace EOS.Resources.Tests;

public class ModelResidencyTests
{
    [Fact]
    public void GetModelResidency_ReturnsUnloaded_ForAModelNeverRouted()
    {
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 3600, modelIdleResidencyTimeoutSeconds: 900, new NoOpModelLoadedEventPublisher(), new NoOpModelUnloadedEventPublisher());

        var status = monitor.GetModelResidency("qwen2.5-coder:7b");

        Assert.Equal(ModelResidencyState.Unloaded, status.State);
        Assert.Null(status.RamFootprintMegabytes);
    }

    [Fact]
    public void RecordInferenceRouted_TransitionsModelToResident()
    {
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 3600, modelIdleResidencyTimeoutSeconds: 900, new NoOpModelLoadedEventPublisher(), new NoOpModelUnloadedEventPublisher());

        monitor.RecordInferenceRouted("qwen2.5-coder:7b");

        var status = monitor.GetModelResidency("qwen2.5-coder:7b");
        Assert.Equal(ModelResidencyState.Resident, status.State);
    }

    [Fact]
    public void RecordInferenceRouted_PublishesModelLoaded_OnFirstObservation()
    {
        var publisher = new CapturingModelLoadedEventPublisher();
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 3600, modelIdleResidencyTimeoutSeconds: 900, publisher, new NoOpModelUnloadedEventPublisher());

        monitor.RecordInferenceRouted("qwen2.5-coder:7b");

        Assert.Equal(1, publisher.CallCount);
        Assert.Equal("qwen2.5-coder:7b", publisher.LastModelId);
    }

    [Fact]
    public void RecordInferenceRouted_DoesNotPublishModelLoaded_OnSubsequentObservationsOfTheSameModel()
    {
        var publisher = new CapturingModelLoadedEventPublisher();
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 3600, modelIdleResidencyTimeoutSeconds: 900, publisher, new NoOpModelUnloadedEventPublisher());
        monitor.RecordInferenceRouted("qwen2.5-coder:7b");

        monitor.RecordInferenceRouted("qwen2.5-coder:7b");

        Assert.Equal(1, publisher.CallCount);
    }

    // WP-022 Recovery Plan Slice R3/Finding F4: no frozen document defines a signal marking the
    // start of a model load distinct from its completion (InferenceRouted fires only after the
    // model is already resident) — there is no legal instant to bookend a real RAM delta.
    // Reporting null is the honest signal, never a fabricated measurement that would always be
    // ~0 regardless of the model's real footprint.
    [Fact]
    public void GetModelResidency_ReportsNullFootprint_BecauseNoLegalMeasurementSourceExists()
    {
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 3600, modelIdleResidencyTimeoutSeconds: 900, new NoOpModelLoadedEventPublisher(), new NoOpModelUnloadedEventPublisher());
        monitor.RecordInferenceRouted("qwen2.5-coder:7b");

        var status = monitor.GetModelResidency("qwen2.5-coder:7b");

        Assert.Null(status.RamFootprintMegabytes);
    }

    [Fact]
    public void RecordInferenceRouted_PublishesModelLoaded_WithTheDisclosedUnmeasurableSentinel()
    {
        var publisher = new CapturingModelLoadedEventPublisher();
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 3600, modelIdleResidencyTimeoutSeconds: 900, publisher, new NoOpModelUnloadedEventPublisher());

        monitor.RecordInferenceRouted("qwen2.5-coder:7b");

        Assert.Equal(0.0, publisher.LastRamFootprintMegabytes);
    }

    [Fact]
    public void GetModelResidency_EvictsToUnloaded_AfterTheIdleResidencyTimeoutElapses()
    {
        var unloadedPublisher = new CapturingModelUnloadedEventPublisher();
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 3600, modelIdleResidencyTimeoutSeconds: 0, new NoOpModelLoadedEventPublisher(), unloadedPublisher);
        monitor.RecordInferenceRouted("qwen2.5-coder:7b");

        // modelIdleResidencyTimeoutSeconds: 0 means any elapsed time already exceeds the timeout.
        var status = monitor.GetModelResidency("qwen2.5-coder:7b");

        Assert.Equal(ModelResidencyState.Unloaded, status.State);
        Assert.Equal(1, unloadedPublisher.CallCount);
        Assert.Equal("qwen2.5-coder:7b", unloadedPublisher.LastModelId);
    }

    [Fact]
    public void GetModelResidency_DoesNotEvict_BeforeTheIdleResidencyTimeoutElapses()
    {
        var unloadedPublisher = new CapturingModelUnloadedEventPublisher();
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 3600, modelIdleResidencyTimeoutSeconds: 900, new NoOpModelLoadedEventPublisher(), unloadedPublisher);
        monitor.RecordInferenceRouted("qwen2.5-coder:7b");

        var status = monitor.GetModelResidency("qwen2.5-coder:7b");

        Assert.Equal(ModelResidencyState.Resident, status.State);
        Assert.Equal(0, unloadedPublisher.CallCount);
    }

    private sealed class CapturingModelLoadedEventPublisher : IModelLoadedEventPublisher
    {
        public int CallCount { get; private set; }
        public string? LastModelId { get; private set; }
        public double LastRamFootprintMegabytes { get; private set; }

        public void PublishModelLoaded(string modelId, double ramFootprintMegabytes)
        {
            CallCount++;
            LastModelId = modelId;
            LastRamFootprintMegabytes = ramFootprintMegabytes;
        }
    }

    private sealed class CapturingModelUnloadedEventPublisher : IModelUnloadedEventPublisher
    {
        public int CallCount { get; private set; }
        public string? LastModelId { get; private set; }

        public void PublishModelUnloaded(string modelId, double ramFootprintMegabytes)
        {
            CallCount++;
            LastModelId = modelId;
        }
    }
}
