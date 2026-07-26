namespace EOS.AIProvider;

public interface IProviderEventLogger
{
    void LogEvent(string message);

    void LogWarning(string message);
}
