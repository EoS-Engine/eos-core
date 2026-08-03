using EOS.Contracts;
using EOS.Resources;

namespace EOS.Resources.Tests;

public class ResourceMonitorTests
{
    [Fact]
    public void Sample_ReturnsNonNegativeCpuUtilization()
    {
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 30);

        var value = monitor.Sample(ResourceType.Cpu);

        Assert.InRange(value, 0.0, 100.0);
    }

    [Fact]
    public void Sample_ReturnsPositiveRamUsed()
    {
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 30);

        var value = monitor.Sample(ResourceType.Ram);

        Assert.True(value > 0);
    }

    [Fact]
    public void Sample_ReturnsPositiveDiskUsed()
    {
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 30);

        var value = monitor.Sample(ResourceType.Disk);

        Assert.True(value > 0);
    }

    [Fact]
    public void Sample_ThrottlesRepeatedCallsWithinInterval()
    {
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 300);

        var first = monitor.Sample(ResourceType.Ram);
        var second = monitor.Sample(ResourceType.Ram);

        Assert.Equal(first, second);
    }

    [Fact]
    public void RecordTaskStarted_IncrementsQueueLengthAndBackgroundTasks()
    {
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 300);
        var taskId = Guid.NewGuid();

        monitor.RecordTaskStarted(taskId);

        Assert.Equal(1, monitor.Sample(ResourceType.QueueLength));
        Assert.Equal(1, monitor.Sample(ResourceType.BackgroundTasks));
    }

    [Fact]
    public void RecordTaskCompleted_DecrementsActiveTaskCount()
    {
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 300);
        var taskId = Guid.NewGuid();
        monitor.RecordTaskStarted(taskId);

        monitor.RecordTaskCompleted(taskId);

        Assert.Equal(0, monitor.Sample(ResourceType.BackgroundTasks));
    }

    [Fact]
    public void RecordTaskCompleted_NeverGoesNegative_WhenCalledWithoutAPriorStart()
    {
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 300);

        monitor.RecordTaskCompleted(Guid.NewGuid());

        Assert.Equal(0, monitor.Sample(ResourceType.BackgroundTasks));
    }

    [Fact]
    public void RecordTaskBlocked_DecrementsActiveTaskCount()
    {
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 300);
        var taskId = Guid.NewGuid();
        monitor.RecordTaskStarted(taskId);

        monitor.RecordTaskBlocked(taskId);

        Assert.Equal(0, monitor.Sample(ResourceType.QueueLength));
    }

    [Fact]
    public void RecordInferenceRouted_IncrementsModelUsage()
    {
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 300);

        monitor.RecordInferenceRouted("qwen2.5-coder:7b");

        Assert.Equal(1, monitor.Sample(ResourceType.ModelUsage));
    }

    [Fact]
    public void RecordInferenceCompleted_DecrementsModelUsage()
    {
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 300);
        monitor.RecordInferenceRouted("qwen2.5-coder:7b");

        monitor.RecordInferenceCompleted("qwen2.5-coder:7b");

        Assert.Equal(0, monitor.Sample(ResourceType.ModelUsage));
    }

    [Fact]
    public void Sample_ReturnsZero_ForCacheUsage_WhenNoCacheTierStoreExists()
    {
        var monitor = new ResourceMonitor(samplingIntervalSeconds: 30);

        var value = monitor.Sample(ResourceType.CacheUsage);

        Assert.Equal(0.0, value);
    }
}
