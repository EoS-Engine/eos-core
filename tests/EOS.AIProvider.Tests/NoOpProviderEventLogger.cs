using EOS.AIProvider;

namespace EOS.AIProvider.Tests;

internal sealed class NoOpProviderEventLogger : IProviderEventLogger
{
    public void LogEvent(string message)
    {
    }

    public void LogWarning(string message)
    {
    }
}
