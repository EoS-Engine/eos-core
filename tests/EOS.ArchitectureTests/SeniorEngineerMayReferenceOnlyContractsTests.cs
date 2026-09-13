using System.Xml.Linq;

namespace EOS.ArchitectureTests;

// Post-Roadmap WP-A: Constitution Part 1 §1.2 (EOS.SeniorEngineer depends on EOS.Contracts;
// never EOS.CTO, EOS.PrincipalEngineer, EOS.TechLead), Part 2 §2.1 rule 2 / fitness rule R-02
// (SeniorEngineer → CTO forbidden), and Part 11 §11.2 (role projects depend on EOS.SDK +
// EOS.Contracts and nothing else infrastructural). The first role project with behaviour makes
// this concretely enforceable: its project-reference set must be exactly { EOS.Contracts }.
public class SeniorEngineerMayReferenceOnlyContractsTests
{
    [Fact]
    public void EOSSeniorEngineer_ReferencesExactlyEOSContracts()
    {
        var projectFile = Path.Combine(FindRepositoryRoot(), "src", "EOS.SeniorEngineer", "EOS.SeniorEngineer.csproj");
        var document = XDocument.Load(projectFile);

        var references = document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")!.Value.Replace('\\', '/'))
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["EOS.Contracts"], references);
        Assert.Empty(document.Descendants("PackageReference"));
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
