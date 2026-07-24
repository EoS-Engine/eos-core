using System.Xml.Linq;

namespace EOS.ArchitectureTests;

public class NoCircularProjectReferencesTests
{
    [Fact]
    public void ProjectReferenceGraph_HasNoCircularReferences()
    {
        var graph = BuildProjectReferenceGraph();

        var cycle = FindCycle(graph);

        Assert.True(cycle is null, $"Circular project reference detected: {string.Join(" -> ", cycle ?? [])}");
    }

    private static Dictionary<string, List<string>> BuildProjectReferenceGraph()
    {
        var repositoryRoot = FindRepositoryRoot();
        var srcDirectory = Path.Combine(repositoryRoot, "src");
        var graph = new Dictionary<string, List<string>>();

        foreach (var projectFile in Directory.EnumerateFiles(srcDirectory, "*.csproj", SearchOption.AllDirectories))
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFile);
            var document = XDocument.Load(projectFile);
            var projectDirectory = Path.GetDirectoryName(projectFile)!;

            var references = document
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")!.Value)
                .Select(relativePath => Path.GetFileNameWithoutExtension(relativePath))
                .ToList();

            graph[projectName] = references;
        }

        return graph;
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

    private static List<string>? FindCycle(Dictionary<string, List<string>> graph)
    {
        var visited = new HashSet<string>();
        var inStack = new HashSet<string>();
        var path = new List<string>();

        foreach (var node in graph.Keys)
        {
            if (Visit(node, graph, visited, inStack, path, out var cycle))
            {
                return cycle;
            }
        }

        return null;
    }

    private static bool Visit(
        string node,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        HashSet<string> inStack,
        List<string> path,
        out List<string>? cycle)
    {
        cycle = null;

        if (inStack.Contains(node))
        {
            var cycleStart = path.IndexOf(node);
            cycle = [.. path[cycleStart..], node];
            return true;
        }

        if (visited.Contains(node))
        {
            return false;
        }

        visited.Add(node);
        inStack.Add(node);
        path.Add(node);

        foreach (var dependency in graph.GetValueOrDefault(node, []))
        {
            if (Visit(dependency, graph, visited, inStack, path, out cycle))
            {
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        inStack.Remove(node);
        return false;
    }
}
