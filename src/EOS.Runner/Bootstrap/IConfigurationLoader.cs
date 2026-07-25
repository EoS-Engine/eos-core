namespace EOS.Runner.Bootstrap;

public interface IConfigurationLoader
{
    string ConfigDirectory { get; }

    T Load<T>(string fileName) where T : class;
}
