using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using EOS.SharedKernel.Configuration;

namespace EOS.Runner.Bootstrap;

public sealed class JsonConfigurationLoader(string configDirectory) : IConfigurationLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public string ConfigDirectory { get; } = configDirectory;

    public T Load<T>(string fileName) where T : class
    {
        var path = Path.Combine(ConfigDirectory, fileName);

        if (!File.Exists(path))
        {
            throw new ConfigurationValidationException($"Configuration file not found: {path}");
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            throw new ConfigurationValidationException($"Unable to read configuration file: {path}", ex);
        }

        T value;
        try
        {
            value = JsonSerializer.Deserialize<T>(json, SerializerOptions)
                ?? throw new ConfigurationValidationException($"Configuration file is empty or null: {path}");
        }
        catch (JsonException ex)
        {
            throw new ConfigurationValidationException($"Configuration file is malformed: {path}", ex);
        }

        Validate(value, path);

        if (value is ProvidersOptions providersOptions)
        {
            foreach (var provider in providersOptions.Providers)
            {
                Validate(provider, path);
            }
        }

        return value;
    }

    public static JsonConfigurationLoader Discover()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EOS.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new ConfigurationValidationException("Could not locate repository root (EOS.slnx not found).");
        }

        return new JsonConfigurationLoader(Path.Combine(directory.FullName, "config"));
    }

    private static void Validate<T>(T value, string path) where T : notnull
    {
        var context = new ValidationContext(value);
        var results = new List<ValidationResult>();

        if (!Validator.TryValidateObject(value, context, results, validateAllProperties: true))
        {
            var errors = string.Join("; ", results.Select(r => r.ErrorMessage));
            throw new ConfigurationValidationException($"Configuration file failed validation: {path} — {errors}");
        }
    }
}
