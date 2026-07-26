using EOS.AIProvider;

namespace EOS.AIProvider.Tests;

internal sealed class RecordingProviderEventLogger : IProviderEventLogger
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages => _messages;

    public void LogEvent(string message) => _messages.Add(message);

    public void LogWarning(string message) => _messages.Add(message);
}
