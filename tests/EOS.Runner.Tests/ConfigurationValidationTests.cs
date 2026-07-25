using EOS.Runner.Bootstrap;
using EOS.SharedKernel.Configuration;

namespace EOS.Runner.Tests;

public class ConfigurationValidationTests : IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("eos-config-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDirectory, recursive: true);

    [Fact]
    public void Load_RejectsMalformedJson()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, "EOS.json"), "{ not valid json");
        var loader = new JsonConfigurationLoader(_tempDirectory);

        Assert.Throws<ConfigurationValidationException>(() => loader.Load<EosOptions>("EOS.json"));
    }

    [Fact]
    public void Load_RejectsMissingRequiredValue()
    {
        File.WriteAllText(
            Path.Combine(_tempDirectory, "EOS.json"),
            """{ "systemName": "EOS", "environment": "Development" }""");
        var loader = new JsonConfigurationLoader(_tempDirectory);

        Assert.Throws<ConfigurationValidationException>(() => loader.Load<EosOptions>("EOS.json"));
    }

    [Fact]
    public void Load_RejectsUnknownProperty()
    {
        File.WriteAllText(
            Path.Combine(_tempDirectory, "EOS.json"),
            """{ "systemName": "EOS", "environment": "Development", "version": "0.1.0", "unknownField": true }""");
        var loader = new JsonConfigurationLoader(_tempDirectory);

        Assert.Throws<ConfigurationValidationException>(() => loader.Load<EosOptions>("EOS.json"));
    }

    [Fact]
    public void Load_RejectsMissingFile()
    {
        var loader = new JsonConfigurationLoader(_tempDirectory);

        Assert.Throws<ConfigurationValidationException>(() => loader.Load<EosOptions>("DoesNotExist.json"));
    }

    [Fact]
    public void Load_RejectsInvalidFieldValue()
    {
        File.WriteAllText(
            Path.Combine(_tempDirectory, "EOS.json"),
            """{ "systemName": "EOS", "environment": "NotARealEnvironment", "version": "0.1.0" }""");
        var loader = new JsonConfigurationLoader(_tempDirectory);

        Assert.Throws<ConfigurationValidationException>(() => loader.Load<EosOptions>("EOS.json"));
    }

    [Fact]
    public void Load_AcceptsValidConfiguration()
    {
        File.WriteAllText(
            Path.Combine(_tempDirectory, "EOS.json"),
            """{ "systemName": "EOS", "environment": "Development", "version": "0.1.0" }""");
        var loader = new JsonConfigurationLoader(_tempDirectory);

        var options = loader.Load<EosOptions>("EOS.json");

        Assert.Equal("EOS", options.SystemName);
    }
}
