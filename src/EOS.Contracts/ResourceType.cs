namespace EOS.Contracts;

/// <summary>
/// Resource-Management-Specification-v1.0 §18.2's Monitored Dimensions table — the seven
/// resource types Resource Monitor samples and Capacity Manager evaluates against Safe/Warning/
/// Critical/Emergency thresholds (§17).
/// </summary>
public enum ResourceType
{
    Cpu,
    Ram,
    Disk,
    ModelUsage,
    QueueLength,
    BackgroundTasks,
    CacheUsage,
}
