using System.Xml.Linq;

namespace EOS.ArchitectureTests;

public class OnlyAllowedProjectsMayReferenceAIProviderTests
{
    private static readonly HashSet<string> AllowedReferencingProjects = new(StringComparer.Ordinal)
    {
        "EOS.AIProvider",
        "EOS.AIProvider.Tests",
        "EOS.ArchitectureTests",

        // EOS.Reasoning is the sole IAIProviderClient consumer (Constitution Part 1 §1.2's
        // explicit carve-out), deferred here from WP-005 and resolved in WP-008.
        "EOS.Reasoning",
        "EOS.Reasoning.Tests",
    };

    [Fact]
    public void EOSAIProvider_IsReferencedOnlyByAllowedProjects()
    {
        var repositoryRoot = FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var projectFile in EnumerateAllProjectFiles(repositoryRoot))
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFile);
            if (AllowedReferencingProjects.Contains(projectName))
            {
                continue;
            }

            var document = XDocument.Load(projectFile);
            var referencesAIProvider = document
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")!.Value.Replace('\\', '/'))
                .Select(Path.GetFileNameWithoutExtension)
                .Any(referencedProject => referencedProject == "EOS.AIProvider");

            if (referencesAIProvider)
            {
                violations.Add(projectName);
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Project(s) not on the allowed list reference EOS.AIProvider: {string.Join(", ", violations)}");
    }

    private static IEnumerable<string> EnumerateAllProjectFiles(string repositoryRoot)
    {
        var srcDirectory = Path.Combine(repositoryRoot, "src");
        var testsDirectory = Path.Combine(repositoryRoot, "tests");

        return Directory.EnumerateFiles(srcDirectory, "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(testsDirectory, "*.csproj", SearchOption.AllDirectories));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EOS.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate repository root (EOS.slnx not found).");
    }
}
