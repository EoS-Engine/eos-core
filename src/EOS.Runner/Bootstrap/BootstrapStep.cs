namespace EOS.Runner.Bootstrap;

public sealed class BootstrapStep(string name, Func<CancellationToken, Task> execute)
{
    public string Name { get; } = name;

    public Task ExecuteAsync(CancellationToken cancellationToken) => execute(cancellationToken);
}
