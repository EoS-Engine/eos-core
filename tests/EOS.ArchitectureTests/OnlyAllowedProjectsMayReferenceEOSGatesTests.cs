using System.Xml.Linq;

namespace EOS.ArchitectureTests;

public class OnlyAllowedProjectsMayReferenceEOSGatesTests
{
    private static readonly HashSet<string> AllowedReferencingProjects = new(StringComparer.Ordinal)
    {
        "EOS.Gates",
        "EOS.Gates.Tests",
        "EOS.ArchitectureTests",

        // Pre-existing, Constitution-declared dependents (EOS-Specification.md Part 1 §1.2),
        // scaffolded since WP-001 — not introduced by WP-006.
        "EOS.PrincipalEngineer",
        "EOS.QA",
        "EOS.Pipeline",
    };

    [Fact]
    public void EOSGates_IsReferencedOnlyByAllowedProjects()
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
            var referencesEOSGates = document
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")!.Value.Replace('\\', '/'))
                .Select(Path.GetFileNameWithoutExtension)
                .Any(referencedProject => referencedProject == "EOS.Gates");

            if (referencesEOSGates)
            {
                violations.Add(projectName);
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Project(s) not on the allowed list reference EOS.Gates: {string.Join(", ", violations)}");
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
